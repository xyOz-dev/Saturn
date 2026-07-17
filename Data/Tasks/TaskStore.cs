using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Saturn.Core.Tasks;

namespace Saturn.Data.Tasks
{
    public class TaskCreateSpec
    {
        public string Title = "";
        public string? Notes;
        public string Scope = TaskScopes.Project;
        public string Board = "default";
        public string Priority = "normal";
        public string CreatedBy = "user";
        public bool AgentAvailable;
        public bool RequiresApproval;
        public bool UserHandoffOnly;
        public List<string> BlockedBy = new();
        public string RecurrenceKind = RecurrenceKinds.None;
        public int? RecurrenceIntervalSeconds;
        public string? RecurrenceCron;
        public string CatchUpPolicy = CatchUpPolicies.RunOnce;
    }

    public class TaskUpdateSpec
    {
        public string? Title;
        public string? Notes;
        public string? Status;
        public string? Priority;
        public string? Scope;
        public string? Board;
        public int? SortOrder;
        public bool? AgentAvailable;
        public bool? RequiresApproval;
        public bool? UserHandoffOnly;
        public List<string>? BlockedBy;
        public string? RecurrenceKind;
        public int? RecurrenceIntervalSeconds;
        public string? RecurrenceCron;
        public string? CatchUpPolicy;
    }

    public class TaskStore : IDisposable
    {
        public TaskRepository Project { get; }
        public TaskRepository Global { get; }

        public event Action<string, SaturnTask>? OnTaskChanged;

        public TaskStore(string? workspacePath = null, string? globalDirectory = null)
        {
            var projectDir = Path.Combine(workspacePath ?? Directory.GetCurrentDirectory(), ".saturn");
            var globalDir = globalDirectory
                ?? Environment.GetEnvironmentVariable("SATURN_CONFIG_DIR")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Saturn");

            Project = new TaskRepository(Path.Combine(projectDir, "tasks.db"), includeRuntimeTables: true);
            Global = new TaskRepository(Path.Combine(globalDir, "tasks.db"), includeRuntimeTables: false);
        }

        public TaskRepository RepoFor(string scope) => scope == TaskScopes.Global ? Global : Project;
        public TaskRepository RepoOf(SaturnTask t) => RepoFor(t.Scope);

        public async Task<SaturnTask?> FindAsync(string id)
        {
            return await Project.GetTaskAsync(id) ?? await Global.GetTaskAsync(id);
        }

        public async Task<SaturnTask> CreateAsync(TaskCreateSpec spec)
        {
            if (string.IsNullOrWhiteSpace(spec.Title))
            {
                throw new ArgumentException("Title is required");
            }
            if (!TaskScopes.All.Contains(spec.Scope))
            {
                throw new ArgumentException($"Invalid scope '{spec.Scope}'");
            }

            var recurrenceError = RecurrenceCalculator.Validate(spec.RecurrenceKind, spec.RecurrenceIntervalSeconds, spec.RecurrenceCron);
            if (recurrenceError != null)
            {
                throw new ArgumentException(recurrenceError);
            }

            var task = new SaturnTask
            {
                Title = spec.Title.Trim(),
                Notes = string.IsNullOrWhiteSpace(spec.Notes) ? null : spec.Notes.Trim(),
                Scope = spec.Scope,
                Board = string.IsNullOrWhiteSpace(spec.Board) ? "default" : spec.Board.Trim(),
                Priority = spec.Priority,
                CreatedBy = spec.CreatedBy,
                AgentAvailable = spec.AgentAvailable,
                RequiresApproval = spec.RequiresApproval,
                UserHandoffOnly = spec.UserHandoffOnly,
                RecurrenceKind = spec.RecurrenceKind,
                RecurrenceIntervalSeconds = spec.RecurrenceIntervalSeconds,
                RecurrenceCron = spec.RecurrenceCron,
                CatchUpPolicy = spec.CatchUpPolicy
            };

            if (task.IsRecurring)
            {
                task.NextRunAt = RecurrenceCalculator.GetNextOccurrenceUtc(
                    task.RecurrenceKind, task.RecurrenceIntervalSeconds, task.RecurrenceCron, DateTime.UtcNow);
            }

            if (spec.BlockedBy.Count > 0)
            {
                await ValidateDependenciesAsync(task.Id, spec.BlockedBy);
            }

            await RepoOf(task).InsertTaskAsync(task);
            if (spec.BlockedBy.Count > 0)
            {
                await RepoOf(task).SetDependenciesAsync(task.Id, spec.BlockedBy.Distinct().ToList());
            }

            OnTaskChanged?.Invoke("created", task);
            return task;
        }

        public async Task<SaturnTask?> UpdateAsync(string id, TaskUpdateSpec spec)
        {
            // Read-modify-write under optimistic concurrency: when the guarded
            // update loses a race, reload the row and reapply the spec so the
            // concurrent writer's committed columns survive.
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var task = await FindAsync(id);
                if (task == null)
                {
                    return null;
                }

                var originalScope = task.Scope;

                if (spec.Title != null && !string.IsNullOrWhiteSpace(spec.Title)) task.Title = spec.Title.Trim();
                if (spec.Notes != null) task.Notes = string.IsNullOrWhiteSpace(spec.Notes) ? null : spec.Notes.Trim();
                if (spec.Priority != null) task.Priority = spec.Priority;
                if (spec.Board != null && !string.IsNullOrWhiteSpace(spec.Board)) task.Board = spec.Board.Trim();
                if (spec.SortOrder.HasValue) task.SortOrder = spec.SortOrder.Value;
                if (spec.AgentAvailable.HasValue) task.AgentAvailable = spec.AgentAvailable.Value;
                if (spec.RequiresApproval.HasValue) task.RequiresApproval = spec.RequiresApproval.Value;
                if (spec.UserHandoffOnly.HasValue) task.UserHandoffOnly = spec.UserHandoffOnly.Value;

                if (spec.Status != null && TaskStatuses.All.Contains(spec.Status))
                {
                    task.Status = spec.Status;
                    task.CompletedAt = TaskStatuses.IsTerminal(spec.Status) ? DateTime.UtcNow : null;
                }

                if (spec.RecurrenceKind != null)
                {
                    var kind = spec.RecurrenceKind;
                    var interval = spec.RecurrenceIntervalSeconds ?? task.RecurrenceIntervalSeconds;
                    var cron = spec.RecurrenceCron ?? task.RecurrenceCron;
                    var error = RecurrenceCalculator.Validate(kind, interval, cron);
                    if (error != null)
                    {
                        throw new ArgumentException(error);
                    }
                    task.RecurrenceKind = kind;
                    task.RecurrenceIntervalSeconds = kind == RecurrenceKinds.Interval ? interval : null;
                    task.RecurrenceCron = kind == RecurrenceKinds.Cron ? cron : null;
                    task.NextRunAt = task.IsRecurring
                        ? RecurrenceCalculator.GetNextOccurrenceUtc(task.RecurrenceKind, task.RecurrenceIntervalSeconds, task.RecurrenceCron, DateTime.UtcNow)
                        : null;
                }
                if (spec.CatchUpPolicy != null) task.CatchUpPolicy = spec.CatchUpPolicy;

                if (spec.BlockedBy != null)
                {
                    await ValidateDependenciesAsync(task.Id, spec.BlockedBy);
                }

                // Scope moves migrate the row between databases.
                if (spec.Scope != null && TaskScopes.All.Contains(spec.Scope) && spec.Scope != originalScope)
                {
                    var sourceRepo = RepoFor(originalScope);
                    var deps = await sourceRepo.GetDependenciesAsync(task.Id);

                    // DeleteTaskAsync also wipes edges where this task is the blocker;
                    // snapshot the dependents' edge lists so they can be restored.
                    var dependentEdges = new Dictionary<string, List<string>>();
                    foreach (var dependentId in await sourceRepo.GetDependentsAsync(task.Id))
                    {
                        dependentEdges[dependentId] = await sourceRepo.GetDependenciesAsync(dependentId);
                    }

                    // Guard the delete with the row's loaded UpdatedAt so a writer
                    // that committed since we read it isn't silently discarded by
                    // the move; reload and reapply the whole spec on conflict.
                    if (!await sourceRepo.DeleteTaskAsync(task.Id, task.UpdatedAt))
                    {
                        continue;
                    }
                    task.Scope = spec.Scope;
                    await RepoOf(task).InsertTaskAsync(task);
                    await RepoOf(task).SetDependenciesAsync(task.Id, deps);

                    foreach (var (dependentId, edges) in dependentEdges)
                    {
                        await sourceRepo.SetDependenciesAsync(dependentId, edges);
                    }
                }
                else if (!await RepoOf(task).UpdateTaskAsync(task))
                {
                    // Lost the race (or the task was deleted): reload and reapply.
                    continue;
                }

                if (spec.BlockedBy != null)
                {
                    await RepoOf(task).SetDependenciesAsync(task.Id, spec.BlockedBy.Distinct().ToList());
                }

                OnTaskChanged?.Invoke("updated", task);
                return task;
            }

            throw new InvalidOperationException("concurrent update, try again");
        }

        public async Task<bool> DeleteAsync(string id)
        {
            var task = await FindAsync(id);
            if (task == null)
            {
                return false;
            }
            await RepoOf(task).DeleteTaskAsync(id);
            OnTaskChanged?.Invoke("deleted", task);
            return true;
        }

        public async Task<List<TaskView>> ListAsync(string? scope = null, string? board = null, string? status = null, bool includeDone = true)
        {
            var tasks = new List<SaturnTask>();
            if (scope == null || scope == TaskScopes.Global)
            {
                tasks.AddRange(await Global.QueryTasksAsync(scope == null ? TaskScopes.Global : scope, board, status));
            }
            if (scope != TaskScopes.Global)
            {
                var projectScope = scope; // null → both project and agent rows live here
                tasks.AddRange(await Project.QueryTasksAsync(projectScope, board, status));
            }

            if (!includeDone)
            {
                tasks = tasks.Where(t => !TaskStatuses.IsTerminal(t.Status)).ToList();
            }

            var views = new List<TaskView>();
            foreach (var task in tasks)
            {
                views.Add(await BuildViewAsync(task));
            }
            return views;
        }

        public async Task<TaskView> BuildViewAsync(SaturnTask task)
        {
            var blockers = await GetBlockersAsync(task.Id, RepoOf(task));
            var openDispatch = (await Project.GetDispatchesForTaskAsync(task.Id)).FirstOrDefault(d => d.CompletedAt == null && !d.Orphaned);
            var waiters = await Project.GetPendingWaitersAsync(task.Id);
            return new TaskView
            {
                Task = task,
                Blocked = blockers.Any(b => !b.Satisfied),
                BlockedBy = blockers,
                RecurrenceDescription = task.IsRecurring
                    ? RecurrenceCalculator.Describe(task.RecurrenceKind, task.RecurrenceIntervalSeconds, task.RecurrenceCron)
                    : null,
                DispatchedTo = openDispatch?.AgentName,
                HasWaiters = waiters.Count > 0
            };
        }

        public async Task<bool> IsBlockedAsync(string id)
        {
            var task = await FindAsync(id);
            if (task == null)
            {
                return false;
            }
            var blockers = await GetBlockersAsync(id, RepoOf(task));
            return blockers.Any(b => !b.Satisfied);
        }

        public async Task<List<TaskBlockerInfo>> GetBlockersAsync(string id, TaskRepository? repo = null)
        {
            repo ??= RepoOf((await FindAsync(id))!);
            var blockerIds = await repo.GetDependenciesAsync(id);
            var infos = new List<TaskBlockerInfo>();
            foreach (var blockerId in blockerIds)
            {
                var blocker = await FindAsync(blockerId);
                if (blocker == null)
                {
                    infos.Add(new TaskBlockerInfo { Id = blockerId, Title = "(deleted)", Status = TaskStatuses.Done, Missing = true, Satisfied = true });
                    continue;
                }

                // Recurring tasks reset to pending after every occurrence, so they
                // never reach a terminal status; dependents are satisfied once the
                // recurring task has completed at least one run.
                var satisfied = TaskStatuses.IsTerminal(blocker.Status)
                    || (blocker.IsRecurring && await HasCompletedRunAsync(blocker));

                infos.Add(new TaskBlockerInfo { Id = blocker.Id, Title = blocker.Title, Status = blocker.Status, Satisfied = satisfied });
            }
            return infos;
        }

        private async Task<bool> HasCompletedRunAsync(SaturnTask task)
        {
            var runs = await RepoOf(task).GetRunsAsync(task.Id);
            return runs.Any(r => r.Outcome != null);
        }

        public async Task<SaturnTask?> CompleteAsync(string id, bool success = true, string? note = null)
        {
            // Reload-and-reapply on conflict: for a recurring task the reloaded row
            // carries a NextRunAt the recurrence sweep may have advanced while we
            // were writing, so this loop never stomps a concurrently claimed
            // occurrence back to its pre-claim schedule.
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var task = await FindAsync(id);
                if (task == null)
                {
                    return null;
                }

                if (task.IsRecurring)
                {
                    var outcome = note ?? (success ? TaskStatuses.Done : TaskStatuses.Failed);
                    var repo = RepoOf(task);
                    var updated = await repo.SetLatestRunOutcomeAsync(task.Id, outcome);
                    if (updated == 0)
                    {
                        // No TaskRuns row exists yet (the task has never fired via the
                        // due-recurrence sweep). Record a run directly so dependents
                        // relying on HasCompletedRunAsync don't stay blocked forever.
                        // Safe across retry attempts: once inserted, the next
                        // SetLatestRunOutcomeAsync matches this row and returns 1.
                        var now = DateTime.UtcNow;
                        await repo.InsertRunAsync(new TaskRun
                        {
                            TaskId = task.Id,
                            ScheduledFor = now,
                            FiredAt = now,
                            Outcome = outcome
                        });
                    }
                    task.Status = TaskStatuses.Pending;
                    task.ClaimStatus = ClaimStatuses.None;
                    task.ClaimedBy = null;
                    task.CompletedAt = null;
                }
                else
                {
                    task.Status = success ? TaskStatuses.Done : TaskStatuses.Failed;
                    task.CompletedAt = DateTime.UtcNow;
                }

                if (!await RepoOf(task).UpdateTaskAsync(task))
                {
                    continue;
                }
                OnTaskChanged?.Invoke("completed", task);
                return task;
            }

            throw new InvalidOperationException("concurrent update, try again");
        }

        public async Task<List<SaturnTask>> GetNewlyUnblockedAsync(string completedId)
        {
            var unblocked = new List<SaturnTask>();
            var dependentIds = (await Project.GetDependentsAsync(completedId))
                .Concat(await Global.GetDependentsAsync(completedId))
                .Distinct();

            foreach (var dependentId in dependentIds)
            {
                var dependent = await FindAsync(dependentId);
                if (dependent == null || TaskStatuses.IsTerminal(dependent.Status))
                {
                    continue;
                }
                if (!await IsBlockedAsync(dependentId))
                {
                    unblocked.Add(dependent);
                }
            }
            return unblocked;
        }

        public async Task<List<string>> GetBoardsAsync(string scope = TaskScopes.Project)
        {
            var tasks = await RepoFor(scope).QueryTasksAsync(scope);
            return tasks.Select(t => t.Board).Distinct().OrderBy(b => b).ToList();
        }

        public async Task<int> ImportLegacyTodosAsync(string? workspacePath = null)
        {
            var todosPath = Path.Combine(workspacePath ?? Directory.GetCurrentDirectory(), ".saturn", "todos.json");
            if (!File.Exists(todosPath))
            {
                return 0;
            }

            var existing = await Project.QueryTasksAsync(TaskScopes.Project);
            if (existing.Count > 0)
            {
                return 0;
            }

            var imported = 0;
            try
            {
                var json = File.ReadAllText(todosPath);
                using var doc = JsonDocument.Parse(json);
                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    var title = element.TryGetProperty("title", out var t) ? t.GetString() : null;
                    if (string.IsNullOrWhiteSpace(title))
                    {
                        continue;
                    }
                    var status = element.TryGetProperty("status", out var s) ? s.GetString() : "pending";
                    var task = new SaturnTask
                    {
                        Title = title,
                        Notes = element.TryGetProperty("notes", out var n) ? n.GetString() : null,
                        Priority = element.TryGetProperty("priority", out var p) ? p.GetString() ?? "normal" : "normal",
                        Status = status == "done" ? TaskStatuses.Done : status == "in_progress" ? TaskStatuses.InProgress : TaskStatuses.Pending,
                        CompletedAt = status == "done" ? DateTime.UtcNow : null
                    };
                    await Project.InsertTaskAsync(task);
                    imported++;
                }
                File.Move(todosPath, todosPath + ".imported", overwrite: true);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Legacy todo import failed: {ex.Message}");
            }
            return imported;
        }

        private async Task ValidateDependenciesAsync(string taskId, List<string> blockedBy)
        {
            foreach (var blockerId in blockedBy)
            {
                if (blockerId == taskId)
                {
                    throw new ArgumentException("A task cannot block itself");
                }
                if (await FindAsync(blockerId) == null)
                {
                    throw new ArgumentException($"Dependency '{blockerId}' does not exist");
                }
            }

            // Cycle check: walk the full dependency graph across both databases.
            var edges = (await Project.GetAllDependenciesAsync())
                .Concat(await Global.GetAllDependenciesAsync())
                .Where(e => e.TaskId != taskId)
                .GroupBy(e => e.TaskId)
                .ToDictionary(g => g.Key, g => g.Select(e => e.BlockedByTaskId).ToList());
            edges[taskId] = blockedBy.ToList();

            var visiting = new HashSet<string>();
            var visited = new HashSet<string>();

            void Visit(string node, List<string> path)
            {
                if (visited.Contains(node)) return;
                if (!visiting.Add(node))
                {
                    throw new ArgumentException($"Dependency cycle detected: {string.Join(" -> ", path)} -> {node}");
                }
                if (edges.TryGetValue(node, out var next))
                {
                    foreach (var n in next)
                    {
                        path.Add(node);
                        Visit(n, path);
                        path.RemoveAt(path.Count - 1);
                    }
                }
                visiting.Remove(node);
                visited.Add(node);
            }

            Visit(taskId, new List<string>());
        }

        public void Dispose()
        {
            Project.Dispose();
            Global.Dispose();
        }
    }
}
