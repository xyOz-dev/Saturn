using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Saturn.Agents.MultiAgent;
using Saturn.Agents.MultiAgent.Objects;
using Saturn.Config;
using Saturn.Data.Tasks;
using Saturn.Tools.Core;
using Saturn.Web;

namespace Saturn.Core.Tasks
{
    // Central brain of the task system: enqueues orchestrator wake prompts,
    // fires recurrences, and surfaces ready/unblocked work. The scheduler and
    // completion events call in; the orchestrator LLM makes all decisions.
    public class TaskCoordinator
    {
        private readonly TaskStore _store;
        private readonly OrchestratorService _orchestrator;
        private readonly EventHub _hub;
        private readonly TaskSystemSettings _settings;

        // A recurrence this far past due at startup counts as "missed" for catch-up policy.
        private static readonly TimeSpan MissedGrace = TimeSpan.FromMinutes(5);

        public TaskCoordinator(TaskStore store, OrchestratorService orchestrator, EventHub hub, TaskSystemSettings settings)
        {
            _store = store;
            _orchestrator = orchestrator;
            _hub = hub;
            _settings = settings;

            _orchestrator.OnIdle += () => _ = SafePumpAsync();
            AgentManager.Instance.OnTaskCompleted += (mgrTaskId, result) =>
                _ = SafeHandleAgentTaskCompletedAsync(mgrTaskId, result);
        }

        public async Task StartAsync()
        {
            await RecoverAsync();
            await ProcessDueRecurrencesAsync();
            await RetryPendingWaitersAsync();
            await PumpWakeQueueAsync();
        }

        public async Task<bool> EnqueueWakeAsync(string kind, string? taskId, string prompt, string? dedupeKey, bool critical = false)
        {
            // The hourly cap only throttles proactive nudges (recurrences, ready
            // tasks). Completions, claim results and recovery notices must always
            // land or their continuations are lost.
            if (!critical)
            {
                var recentCount = await _store.Project.CountRecentWakesAsync(DateTime.UtcNow.AddHours(-1));
                if (recentCount >= _settings.MaxWakesPerHour)
                {
                    _hub.Publish("wake.suppressed", new { kind, taskId, reason = $"MaxWakesPerHour ({_settings.MaxWakesPerHour}) reached" });
                    return false;
                }
            }

            var enqueued = await _store.Project.TryEnqueueWakeAsync(new WakeItem
            {
                Kind = kind,
                TaskId = taskId,
                DedupeKey = dedupeKey,
                Prompt = prompt
            });

            if (enqueued)
            {
                _hub.Publish("wake.enqueued", new { kind, taskId });
                _ = SafePumpAsync();
            }
            return enqueued;
        }

        public async Task PumpWakeQueueAsync()
        {
            if (_orchestrator.IsBusy)
            {
                return;
            }

            var wake = await _store.Project.PeekOldestWakeAsync();
            if (wake == null)
            {
                return;
            }

            if (_orchestrator.TrySend($"[Saturn Scheduler] {wake.Prompt}", "scheduler"))
            {
                await _store.Project.MarkWakeDeliveredAsync(wake.Id);
                _hub.Publish("wake.delivered", new { id = wake.Id, kind = wake.Kind, taskId = wake.TaskId });
            }
        }

        public async Task ProcessDueRecurrencesAsync()
        {
            var now = DateTime.UtcNow;
            foreach (var repo in new[] { _store.Project, _store.Global })
            {
                foreach (var task in await repo.GetDueRecurringAsync(now))
                {
                    var scheduledFor = task.NextRunAt!.Value;
                    var next = RecurrenceCalculator.GetNextOccurrenceUtc(
                        task.RecurrenceKind, task.RecurrenceIntervalSeconds, task.RecurrenceCron, now);

                    // Optimistic claim: only one instance (or sweep) fires each occurrence.
                    if (!await repo.TryClaimRecurrenceAsync(task.Id, scheduledFor, next, now))
                    {
                        continue;
                    }

                    var missed = now - scheduledFor > MissedGrace;
                    if (missed && task.CatchUpPolicy == CatchUpPolicies.Skip)
                    {
                        _hub.Publish("task.due", new { taskId = task.Id, title = task.Title, skipped = true });
                        continue;
                    }

                    await repo.InsertRunAsync(new TaskRun { TaskId = task.Id, ScheduledFor = scheduledFor });
                    _hub.Publish("task.due", new { taskId = task.Id, title = task.Title, skipped = false });

                    var missedNote = missed
                        ? $" This occurrence was originally scheduled for {scheduledFor:u} and is being delivered late (missed while Saturn was not running)."
                        : "";
                    await EnqueueWakeAsync(
                        WakeKinds.RecurrenceDue,
                        task.Id,
                        $"Recurring task '{task.Title}' ({task.Id}) is due.{missedNote} " +
                        $"Notes: {task.Notes ?? "(none)"}. Use list_tasks/claim_task/dispatch_task to act on it, " +
                        "and complete_task when the work is done.",
                        $"recur:{task.Id}:{scheduledFor:yyyy-MM-ddTHH:mm}");
                }
            }
        }

        public async Task ProcessReadyTasksAsync()
        {
            var views = await _store.ListAsync(status: TaskStatuses.Pending, includeDone: false);
            foreach (var view in views)
            {
                var task = view.Task;
                if (!task.AgentAvailable || task.UserHandoffOnly || view.Blocked || task.IsRecurring)
                {
                    continue;
                }
                if (task.ClaimStatus is ClaimStatuses.Denied or ClaimStatuses.PendingApproval)
                {
                    continue;
                }
                if (view.DispatchedTo != null)
                {
                    continue;
                }

                await EnqueueWakeAsync(
                    WakeKinds.TaskReady,
                    task.Id,
                    $"Task '{task.Title}' ({task.Id}) is marked agent-available and ready to be worked on. " +
                    $"Priority: {task.Priority}. Notes: {task.Notes ?? "(none)"}. " +
                    (task.RequiresApproval
                        ? "It requires user approval: call claim_task first and wait for the approval result."
                        : "Use claim_task then dispatch_task to hand it to a sub-agent, or do it yourself."),
                    // Bucketed by hour: delivered wake rows are never deleted, so a
                    // bare task id would allow exactly one nudge per task, ever.
                    $"ready:{task.Id}:{DateTime.UtcNow:yyyyMMddHH}");
            }
        }

        public async Task HandleSaturnTaskCompletedAsync(SaturnTask completed)
        {
            var unblocked = await _store.GetNewlyUnblockedAsync(completed.Id);
            foreach (var task in unblocked)
            {
                _hub.Publish("task.unblocked", new { taskId = task.Id, title = task.Title });
                await EnqueueWakeAsync(
                    WakeKinds.TaskUnblocked,
                    task.Id,
                    $"Task '{task.Title}' ({task.Id}) is no longer blocked — its dependency '{completed.Title}' ({completed.Id}) completed. " +
                    "It can now be worked on.",
                    $"unblock:{task.Id}:{completed.Id}");
            }
        }

        // ---------- Waiters ----------

        public async Task<(bool registered, string message)> RegisterWaiterAsync(string targetId, string? promptTemplate)
        {
            var caller = AgentContext.Current;
            var isSubAgent = caller?.ManagerAgentId != null;

            // Short-circuit when the target already finished.
            var (done, resultText, success) = await ResolveTargetResultAsync(targetId);
            if (done)
            {
                return (false, $"Target {targetId} already completed (success={success}). Result:\n{resultText}");
            }

            var waiter = new TaskWaiter
            {
                WaitTargetKind = targetId.StartsWith("tk_") ? "saturn_task" : "agent_task",
                WaitTargetId = targetId,
                WaiterKind = isSubAgent ? "agent" : "orchestrator",
                WaiterAgentId = caller?.ManagerAgentId,
                WaiterAgentName = caller?.AgentName,
                PromptTemplate = promptTemplate
            };
            await _store.Project.InsertWaiterAsync(waiter);
            _hub.Publish("waiter.registered", new { waiterId = waiter.Id, targetId, waiterKind = waiter.WaiterKind, agentName = waiter.WaiterAgentName });

            return (true, isSubAgent
                ? $"Waiter registered on {targetId}. End your turn now with a brief status report; you will be re-prompted automatically with the result when it completes."
                : $"Waiter registered on {targetId}. You will receive a scheduler message when it completes; you can end this turn.");
        }

        public async Task RetryPendingWaitersAsync()
        {
            foreach (var waiter in await _store.Project.GetPendingWaitersAsync())
            {
                var (done, resultText, success) = await ResolveTargetResultAsync(waiter.WaitTargetId);
                if (done)
                {
                    await DeliverWaiterAsync(waiter, resultText!, success);
                }
            }
        }

        internal async Task DeliverWaiterAsync(TaskWaiter waiter, string resultText, bool success)
        {
            var prompt = waiter.PromptTemplate ??
                $"[Continuation] The task you were waiting on ({waiter.WaitTargetId}) completed (success={success}). Result:\n{resultText}\n\nContinue your work from where you left off.";

            if (waiter.WaiterKind == "agent" && waiter.WaiterAgentId != null)
            {
                var status = AgentManager.Instance.GetAgentStatus(waiter.WaiterAgentId);
                if (status.Exists && status.IsIdle)
                {
                    // Claim the waiter first so concurrent sweeps deliver at most once,
                    // but put it back if the delivery itself fails.
                    if (await _store.Project.MarkWaiterDeliveredAsync(waiter.Id))
                    {
                        try
                        {
                            await AgentManager.Instance.HandOffTask(waiter.WaiterAgentId, prompt);
                            _hub.Publish("waiter.delivered", new { waiterId = waiter.Id, to = waiter.WaiterAgentName });
                        }
                        catch (Exception)
                        {
                            // Agent vanished between the status check and the handoff;
                            // the next sweep re-resolves it (and falls back if gone).
                            await _store.Project.ResetWaiterDeliveryAsync(waiter.Id);
                        }
                    }
                    return;
                }
                if (status.Exists)
                {
                    // Agent busy: leave pending; the scheduler retries next sweep.
                    await _store.Project.IncrementWaiterAttemptsAsync(waiter.Id);
                    return;
                }

                // Agent is gone (terminated or process restarted): route to the orchestrator.
                if (await _store.Project.MarkWaiterDeliveredAsync(waiter.Id))
                {
                    try
                    {
                        await EnqueueWakeAsync(
                            WakeKinds.WaiterFallback,
                            waiter.WaitTargetId.StartsWith("tk_") ? waiter.WaitTargetId : null,
                            $"Agent '{waiter.WaiterAgentName ?? waiter.WaiterAgentId}' was waiting on {waiter.WaitTargetId}, which completed (success={success}), " +
                            $"but that agent no longer exists. Result:\n{resultText}\n\nDecide how to proceed with its work.",
                            $"waiterfb:{waiter.Id}",
                            critical: true);
                    }
                    catch (Exception)
                    {
                        await _store.Project.ResetWaiterDeliveryAsync(waiter.Id);
                    }
                }
                return;
            }

            // Orchestrator waiter.
            if (await _store.Project.MarkWaiterDeliveredAsync(waiter.Id))
            {
                try
                {
                    await EnqueueWakeAsync(
                        WakeKinds.TaskCompleted,
                        waiter.WaitTargetId.StartsWith("tk_") ? waiter.WaitTargetId : null,
                        $"The task you were waiting on ({waiter.WaitTargetId}) completed (success={success}). Result:\n{resultText}",
                        $"waiter:{waiter.Id}",
                        critical: true);
                }
                catch (Exception)
                {
                    await _store.Project.ResetWaiterDeliveryAsync(waiter.Id);
                }
            }
        }

        private async Task<(bool done, string? resultText, bool success)> ResolveTargetResultAsync(string targetId)
        {
            if (targetId.StartsWith("tk_"))
            {
                var task = await _store.FindAsync(targetId);
                if (task == null)
                {
                    return (true, "(task no longer exists)", false);
                }
                if (!TaskStatuses.IsTerminal(task.Status))
                {
                    return (false, null, false);
                }
                var dispatches = await _store.Project.GetDispatchesForTaskAsync(targetId);
                var latest = dispatches.FirstOrDefault(d => d.Result != null);
                return (true, latest?.Result ?? $"(task marked {task.Status} without a dispatch result)", task.Status == TaskStatuses.Done);
            }

            var dispatch = await _store.Project.GetDispatchByManagerTaskIdAsync(targetId);
            if (dispatch?.CompletedAt != null)
            {
                return (true, dispatch.Result ?? "(no result recorded)", dispatch.Success ?? false);
            }
            var inMemory = AgentManager.Instance.GetTaskResult(targetId);
            if (inMemory != null)
            {
                return (true, inMemory.Result, inMemory.Success);
            }

            // Raw handoff results live only in memory. If no agent is currently
            // working on this task id, the result is unrecoverable (restart or
            // cleared results) — resolve as lost rather than waiting forever.
            var stillRunning = AgentManager.Instance.GetAgentContexts()
                .Any(c => c.CurrentTask?.Id == targetId);
            if (!stillRunning)
            {
                return (true, "(the task's result was lost — the process likely restarted before it was collected; re-run the work if it matters)", false);
            }
            return (false, null, false);
        }

        // Reload-mutate-write loop over the optimistic concurrency guard in
        // UpdateTaskAsync. mutate returns false to skip the write when its
        // precondition no longer holds on the freshly loaded row; the freshest
        // row is returned either way, or null when the task no longer exists.
        private async Task<SaturnTask?> UpdateTaskWithRetryAsync(string taskId, Func<SaturnTask, bool> mutate)
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var task = await _store.FindAsync(taskId);
                if (task == null)
                {
                    return null;
                }
                if (!mutate(task))
                {
                    return task;
                }
                if (await _store.RepoOf(task).UpdateTaskAsync(task))
                {
                    return task;
                }
            }
            throw new InvalidOperationException("concurrent update, try again");
        }

        // ---------- Dispatch lifecycle ----------

        public async Task<(bool ok, string message, string? dispatchId)> DispatchTaskAsync(string taskId, string agentId, string agentName, bool userInitiated = false)
        {
            var task = await _store.FindAsync(taskId);
            if (task == null)
            {
                return (false, $"Task {taskId} not found", null);
            }
            if (TaskStatuses.IsTerminal(task.Status))
            {
                return (false, $"Task {taskId} is already {task.Status}", null);
            }

            // Policy gates live here, not just in the tool precheck, so no caller
            // can bypass them. A dispatch the user performs directly is exempt:
            // the gates exist to keep decisions in the user's hands.
            if (!userInitiated)
            {
                if (task.UserHandoffOnly)
                {
                    return (false, $"Task {taskId} is user-handoff-only; the user must dispatch it from the web UI.", null);
                }
                if (task.ClaimStatus == ClaimStatuses.PendingApproval)
                {
                    return (false, $"Task {taskId} is awaiting the user's claim approval.", null);
                }
                if (task.ClaimStatus == ClaimStatuses.Denied)
                {
                    return (false, $"Task {taskId} claim was denied by the user; do not work on it.", null);
                }
                if (task.RequiresApproval && task.ClaimStatus != ClaimStatuses.Approved)
                {
                    return (false, $"Task {taskId} requires approval — claim it and wait for the user's decision first.", null);
                }
            }
            if (await _store.IsBlockedAsync(taskId))
            {
                var blockers = await _store.GetBlockersAsync(taskId);
                return (false, $"Task {taskId} is blocked by: {string.Join(", ", blockers.Where(b => !b.Satisfied).Select(b => $"{b.Title} ({b.Id})"))}", null);
            }
            var openDispatches = (await _store.Project.GetDispatchesForTaskAsync(taskId)).Where(d => d.CompletedAt == null && !d.Orphaned).ToList();
            if (openDispatches.Count > 0)
            {
                return (false, $"Task {taskId} is already dispatched to {openDispatches[0].AgentName}", null);
            }

            var dispatch = await _store.Project.InsertDispatchAsync(new TaskDispatch
            {
                TaskId = taskId,
                AgentId = agentId,
                AgentName = agentName
            });

            var taskPrompt =
                $"You have been dispatched Saturn task '{task.Title}' ({task.Id}).\n" +
                $"Priority: {task.Priority}\nNotes: {task.Notes ?? "(none)"}\n\n" +
                "Complete this task. Your final report becomes the task result.";

            string mgrTaskId;
            try
            {
                // Persist the manager task id and in-progress status before the
                // agent starts: a fast-failing agent completes concurrently and
                // its completion handler must find the dispatch row ready.
                mgrTaskId = await AgentManager.Instance.HandOffTask(agentId, taskPrompt, onBeforeStart: async id =>
                {
                    await _store.Project.SetDispatchManagerTaskIdAsync(dispatch.Id, id);
                    await UpdateTaskWithRetryAsync(task.Id, t =>
                    {
                        t.Status = TaskStatuses.InProgress;
                        t.ClaimedBy = agentName;
                        return true;
                    });
                });
            }
            catch (InvalidOperationException ex)
            {
                await _store.Project.MarkDispatchOrphanedAsync(dispatch.Id);
                return (false, ex.Message, null);
            }

            _hub.Publish("task.dispatched", new { taskId, agentId, agentName });

            return (true, $"Task {taskId} dispatched to {agentName} ({agentId}); manager task {mgrTaskId}", dispatch.Id);
        }

        internal async Task SafeHandleAgentTaskCompletedAsync(string mgrTaskId, AgentTaskResult result)
        {
            try
            {
                await HandleAgentTaskCompletedAsync(mgrTaskId, result);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Task completion handling error: {ex.Message}");
            }
        }

        internal async Task HandleAgentTaskCompletedAsync(string mgrTaskId, AgentTaskResult result)
        {
            var dispatch = await _store.Project.GetDispatchByManagerTaskIdAsync(mgrTaskId);
            if (dispatch != null && dispatch.CompletedAt == null)
            {
                await _store.Project.CompleteDispatchAsync(dispatch.Id, result.Success, result.Result);

                var task = await _store.FindAsync(dispatch.TaskId);
                if (task != null && !TaskStatuses.IsTerminal(task.Status))
                {
                    await _store.CompleteAsync(task.Id, result.Success,
                        $"dispatched to {result.AgentName}: {(result.Success ? "done" : "failed")}");
                }

                await EnqueueWakeAsync(
                    WakeKinds.TaskCompleted,
                    dispatch.TaskId,
                    $"Dispatched task '{task?.Title ?? dispatch.TaskId}' ({dispatch.TaskId}) was completed by {result.AgentName} (success={result.Success}). " +
                    $"Result:\n{Truncate(result.Result, 2000)}",
                    $"complete:{dispatch.Id}",
                    critical: true);
            }

            // Deliver waiters watching either the Saturn task or the raw manager task id.
            var targets = new List<string> { mgrTaskId };
            if (dispatch != null)
            {
                targets.Add(dispatch.TaskId);
            }
            foreach (var target in targets)
            {
                foreach (var waiter in await _store.Project.GetPendingWaitersAsync(target))
                {
                    await DeliverWaiterAsync(waiter, result.Result, result.Success);
                }
            }
        }

        // ---------- Claiming ----------

        // Assigned in Phase 5 to raise a typed user-approval request; until then
        // flagged claims sit in pending_approval visible in the UI.
        public Func<SaturnTask, Task>? OnClaimApprovalNeeded { get; set; }

        public async Task<(string status, string message)> ClaimTaskAsync(string taskId)
        {
            var task = await _store.FindAsync(taskId);
            if (task == null)
            {
                return ("error", $"Task {taskId} not found");
            }
            if (TaskStatuses.IsTerminal(task.Status))
            {
                return ("error", $"Task {taskId} is already {task.Status}");
            }
            if (task.UserHandoffOnly)
            {
                return ("refused", $"Task {taskId} is marked 'wait for user handoff' — only the user can dispatch it.");
            }
            if (await _store.IsBlockedAsync(taskId))
            {
                return ("refused", $"Task {taskId} is blocked by incomplete dependencies.");
            }
            if (task.ClaimStatus == ClaimStatuses.Approved)
            {
                return ("claimed", $"Task {taskId} is already claimed by {task.ClaimedBy}.");
            }
            if (task.ClaimStatus == ClaimStatuses.PendingApproval)
            {
                return ("pending_approval", $"Task {taskId} claim is already awaiting user approval.");
            }

            if (task.RequiresApproval)
            {
                task = await UpdateTaskWithRetryAsync(task.Id, t =>
                {
                    t.ClaimStatus = ClaimStatuses.PendingApproval;
                    return true;
                });
                if (task == null)
                {
                    return ("error", $"Task {taskId} not found");
                }
                _hub.Publish("tasks.changed", new { taskId = task.Id, scope = task.Scope, board = task.Board, change = "claim_pending" });
                if (OnClaimApprovalNeeded != null)
                {
                    await OnClaimApprovalNeeded(task);
                }
                return ("pending_approval",
                    $"Task {taskId} requires user approval. The request is now in the user's approval queue; " +
                    "you will receive a scheduler message when it is approved or denied. Do not start work on it yet.");
            }

            task = await UpdateTaskWithRetryAsync(task.Id, t =>
            {
                t.ClaimStatus = ClaimStatuses.Approved;
                t.ClaimedBy = "orchestrator";
                return true;
            });
            if (task == null)
            {
                return ("error", $"Task {taskId} not found");
            }
            _hub.Publish("tasks.changed", new { taskId = task.Id, scope = task.Scope, board = task.Board, change = "claimed" });
            return ("claimed", $"Task {taskId} claimed. Work on it yourself or dispatch_task it to a sub-agent.");
        }

        public async Task ResolveClaimAsync(string taskId, bool approved)
        {
            var applied = false;
            SaturnTask? task;
            try
            {
                task = await UpdateTaskWithRetryAsync(taskId, t =>
                {
                    if (t.ClaimStatus != ClaimStatuses.PendingApproval)
                    {
                        applied = false;
                        return false;
                    }
                    t.ClaimStatus = approved ? ClaimStatuses.Approved : ClaimStatuses.Denied;
                    t.ClaimedBy = approved ? "orchestrator" : null;
                    applied = true;
                    return true;
                });
            }
            catch (InvalidOperationException ex)
            {
                // Called fire-and-forget from the web approval callback, so a
                // conflict that outlasts the retry budget has nowhere else to
                // surface; log it instead of losing it as an unobserved fault.
                Console.Error.WriteLine($"Resolve claim for task {taskId} failed: {ex.Message}");
                return;
            }
            if (task == null || !applied)
            {
                return;
            }
            _hub.Publish("tasks.changed", new { taskId = task.Id, scope = task.Scope, board = task.Board, change = approved ? "claim_approved" : "claim_denied" });

            await EnqueueWakeAsync(
                WakeKinds.ClaimResolved,
                task.Id,
                approved
                    ? $"Your claim on task '{task.Title}' ({task.Id}) was APPROVED by the user. You may now work on it or dispatch it."
                    : $"Your claim on task '{task.Title}' ({task.Id}) was DENIED by the user. Do not work on it.",
                $"claim:{task.Id}:{DateTime.UtcNow:yyyyMMddHHmmss}",
                critical: true);
        }

        // ---------- Recovery ----------

        internal async Task RecoverAsync()
        {
            foreach (var dispatch in await _store.Project.GetOpenDispatchesAsync())
            {
                var agentAlive = dispatch.AgentId != null && AgentManager.Instance.GetAgentStatus(dispatch.AgentId).Exists;
                if (agentAlive)
                {
                    continue;
                }

                await _store.Project.MarkDispatchOrphanedAsync(dispatch.Id);
                SaturnTask? task = null;
                try
                {
                    task = await UpdateTaskWithRetryAsync(dispatch.TaskId, t =>
                    {
                        if (t.Status != TaskStatuses.InProgress)
                        {
                            return false;
                        }
                        t.Status = TaskStatuses.Pending;
                        return true;
                    });
                }
                catch (InvalidOperationException ex)
                {
                    // Keep recovering the remaining dispatches; the wake below still
                    // tells the orchestrator this dispatch was interrupted.
                    Console.Error.WriteLine($"Recovery update for task {dispatch.TaskId} failed: {ex.Message}");
                    task = await _store.FindAsync(dispatch.TaskId);
                }

                await EnqueueWakeAsync(
                    WakeKinds.OrphanRecovered,
                    dispatch.TaskId,
                    $"Dispatch of task '{task?.Title ?? dispatch.TaskId}' ({dispatch.TaskId}) to agent '{dispatch.AgentName}' was interrupted " +
                    "(the process restarted before it finished). The task is back in pending; re-dispatch it if appropriate.",
                    $"orphan:{dispatch.Id}",
                    critical: true);
            }

            // Claim approvals live only in the in-memory web queue, so any claim
            // that was pending when the process died must be raised again or the
            // task stays in pending_approval with nothing for the user to act on.
            if (OnClaimApprovalNeeded != null)
            {
                var views = await _store.ListAsync(includeDone: false);
                foreach (var view in views.Where(v => v.Task.ClaimStatus == ClaimStatuses.PendingApproval))
                {
                    await OnClaimApprovalNeeded(view.Task);
                }
            }
        }

        private static string Truncate(string value, int max)
        {
            return value.Length <= max ? value : value[..max] + "…(truncated)";
        }

        private async Task SafePumpAsync()
        {
            try
            {
                await PumpWakeQueueAsync();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Wake pump error: {ex.Message}");
            }
        }
    }
}
