using System;
using System.Linq;
using System.Threading.Tasks;
using Saturn.Config;
using Saturn.Data.Tasks;
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
        }

        public async Task StartAsync()
        {
            await ProcessDueRecurrencesAsync();
            await PumpWakeQueueAsync();
        }

        public async Task<bool> EnqueueWakeAsync(string kind, string? taskId, string prompt, string? dedupeKey)
        {
            var recentCount = await _store.Project.CountRecentWakesAsync(DateTime.UtcNow.AddHours(-1));
            if (recentCount >= _settings.MaxWakesPerHour)
            {
                _hub.Publish("wake.suppressed", new { kind, taskId, reason = $"MaxWakesPerHour ({_settings.MaxWakesPerHour}) reached" });
                return false;
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
                    $"ready:{task.Id}");
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
