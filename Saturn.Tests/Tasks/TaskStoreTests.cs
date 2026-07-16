using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Saturn.Data.Tasks;
using Xunit;

namespace Saturn.Tests.Tasks
{
    public class TaskStoreTests : IDisposable
    {
        private readonly string _workspaceDir;
        private readonly string _globalDir;
        private readonly TaskStore _store;

        public TaskStoreTests()
        {
            var root = Path.Combine(Path.GetTempPath(), $"SaturnTaskStoreTest_{Guid.NewGuid():N}");
            _workspaceDir = Path.Combine(root, "workspace");
            _globalDir = Path.Combine(root, "global");
            Directory.CreateDirectory(_workspaceDir);
            Directory.CreateDirectory(_globalDir);
            _store = new TaskStore(_workspaceDir, _globalDir);
        }

        public void Dispose()
        {
            _store.Dispose();
            try
            {
                Directory.Delete(Path.GetDirectoryName(_workspaceDir)!, recursive: true);
            }
            catch
            {
                // SQLite may briefly hold the file on Windows; temp dirs get cleaned by the OS.
            }
        }

        private Task<SaturnTask> CreateTaskAsync(string title, Action<TaskCreateSpec>? configure = null)
        {
            var spec = new TaskCreateSpec { Title = title };
            configure?.Invoke(spec);
            return _store.CreateAsync(spec);
        }

        [Fact]
        public async Task CreateAsync_PersistsAndFindsTask()
        {
            var task = await CreateTaskAsync("write docs");

            var found = await _store.FindAsync(task.Id);
            found.Should().NotBeNull();
            found!.Title.Should().Be("write docs");
            found.Status.Should().Be(TaskStatuses.Pending);
        }

        [Fact]
        public async Task CreateAsync_RequiresTitle()
        {
            var act = () => _store.CreateAsync(new TaskCreateSpec { Title = "  " });
            await act.Should().ThrowAsync<ArgumentException>();
        }

        [Fact]
        public async Task CreateAsync_RejectsUnknownDependency()
        {
            var act = () => CreateTaskAsync("dependent", s => s.BlockedBy = new List<string> { "tk_missing" });
            await act.Should().ThrowAsync<ArgumentException>().WithMessage("*does not exist*");
        }

        [Fact]
        public async Task UpdateAsync_RejectsSelfDependency()
        {
            var task = await CreateTaskAsync("self");
            var act = () => _store.UpdateAsync(task.Id, new TaskUpdateSpec { BlockedBy = new List<string> { task.Id } });
            await act.Should().ThrowAsync<ArgumentException>().WithMessage("*cannot block itself*");
        }

        [Fact]
        public async Task UpdateAsync_RejectsDependencyCycles()
        {
            var a = await CreateTaskAsync("a");
            var b = await CreateTaskAsync("b", s => s.BlockedBy = new List<string> { a.Id });
            var c = await CreateTaskAsync("c", s => s.BlockedBy = new List<string> { b.Id });

            var act = () => _store.UpdateAsync(a.Id, new TaskUpdateSpec { BlockedBy = new List<string> { c.Id } });
            await act.Should().ThrowAsync<ArgumentException>().WithMessage("*cycle*");
        }

        [Fact]
        public async Task CompletingBlocker_UnblocksDependent()
        {
            var blocker = await CreateTaskAsync("blocker");
            var dependent = await CreateTaskAsync("dependent", s => s.BlockedBy = new List<string> { blocker.Id });

            (await _store.IsBlockedAsync(dependent.Id)).Should().BeTrue();

            await _store.CompleteAsync(blocker.Id);

            (await _store.IsBlockedAsync(dependent.Id)).Should().BeFalse();
            var unblocked = await _store.GetNewlyUnblockedAsync(blocker.Id);
            unblocked.Select(t => t.Id).Should().ContainSingle().Which.Should().Be(dependent.Id);
        }

        [Fact]
        public async Task DependentStaysBlocked_WhileAnotherBlockerIsOpen()
        {
            var first = await CreateTaskAsync("first");
            var second = await CreateTaskAsync("second");
            var dependent = await CreateTaskAsync("dependent", s => s.BlockedBy = new List<string> { first.Id, second.Id });

            await _store.CompleteAsync(first.Id);

            (await _store.IsBlockedAsync(dependent.Id)).Should().BeTrue();
            (await _store.GetNewlyUnblockedAsync(first.Id)).Should().BeEmpty();
        }

        [Fact]
        public async Task DeletedBlocker_CountsAsSatisfied()
        {
            var blocker = await CreateTaskAsync("blocker");
            var dependent = await CreateTaskAsync("dependent", s => s.BlockedBy = new List<string> { blocker.Id });

            await _store.DeleteAsync(blocker.Id);

            (await _store.IsBlockedAsync(dependent.Id)).Should().BeFalse();
        }

        [Fact]
        public async Task RecurringBlocker_BlocksUntilARunCompletes()
        {
            var recurring = await CreateTaskAsync("nightly job", s =>
            {
                s.RecurrenceKind = RecurrenceKinds.Interval;
                s.RecurrenceIntervalSeconds = 3600;
            });
            var dependent = await CreateTaskAsync("after nightly", s => s.BlockedBy = new List<string> { recurring.Id });

            (await _store.IsBlockedAsync(dependent.Id)).Should().BeTrue();

            // Simulate one fired occurrence completing.
            await _store.Project.InsertRunAsync(new TaskRun { TaskId = recurring.Id, ScheduledFor = DateTime.UtcNow });
            await _store.CompleteAsync(recurring.Id, success: true);

            (await _store.IsBlockedAsync(dependent.Id)).Should().BeFalse();
            var blockers = await _store.GetBlockersAsync(dependent.Id);
            blockers.Should().ContainSingle().Which.Satisfied.Should().BeTrue();
        }

        [Fact]
        public async Task CompleteAsync_RecurringTask_ResetsToPendingAndRecordsOutcome()
        {
            var recurring = await CreateTaskAsync("hourly job", s =>
            {
                s.RecurrenceKind = RecurrenceKinds.Interval;
                s.RecurrenceIntervalSeconds = 3600;
            });
            await _store.Project.InsertRunAsync(new TaskRun { TaskId = recurring.Id, ScheduledFor = DateTime.UtcNow });

            var changes = new List<(string Change, string Status)>();
            _store.OnTaskChanged += (change, task) => changes.Add((change, task.Status));

            var completed = await _store.CompleteAsync(recurring.Id, success: true);

            completed!.Status.Should().Be(TaskStatuses.Pending);
            completed.CompletedAt.Should().BeNull();
            completed.ClaimStatus.Should().Be(ClaimStatuses.None);

            // The completion event must still fire so dependents get their unblock sweep.
            changes.Should().ContainSingle(c => c.Change == "completed");

            var runs = await _store.Project.GetRunsAsync(recurring.Id);
            runs.Should().ContainSingle().Which.Outcome.Should().NotBeNull();
        }

        [Fact]
        public async Task CompleteAsync_NonRecurringTask_BecomesTerminal()
        {
            var task = await CreateTaskAsync("one shot");

            var done = await _store.CompleteAsync(task.Id, success: false, note: "gave up");

            done!.Status.Should().Be(TaskStatuses.Failed);
            done.CompletedAt.Should().NotBeNull();
        }

        [Fact]
        public async Task UpdateAsync_ScopeChange_MovesTaskBetweenDatabases()
        {
            var task = await CreateTaskAsync("mover");

            await _store.UpdateAsync(task.Id, new TaskUpdateSpec { Scope = TaskScopes.Global });

            (await _store.Project.GetTaskAsync(task.Id)).Should().BeNull();
            var moved = await _store.Global.GetTaskAsync(task.Id);
            moved.Should().NotBeNull();
            moved!.Scope.Should().Be(TaskScopes.Global);
        }

        [Fact]
        public async Task UpdateAsync_ScopeChange_PreservesDependentsOfTheMovedTask()
        {
            var blocker = await CreateTaskAsync("mover blocker");
            var dependent = await CreateTaskAsync("stays behind", s => s.BlockedBy = new List<string> { blocker.Id });

            await _store.UpdateAsync(blocker.Id, new TaskUpdateSpec { Scope = TaskScopes.Global });

            // The dependent's edge lives in the source database and must survive the move.
            (await _store.IsBlockedAsync(dependent.Id)).Should().BeTrue();

            await _store.CompleteAsync(blocker.Id);
            (await _store.IsBlockedAsync(dependent.Id)).Should().BeFalse();
        }

        [Fact]
        public async Task UpdateAsync_ScopeChange_MigratesRunHistorySoRecurringDependentsStayUnblocked()
        {
            var recurring = await CreateTaskAsync("nightly job", s =>
            {
                s.RecurrenceKind = RecurrenceKinds.Interval;
                s.RecurrenceIntervalSeconds = 3600;
            });
            var dependent = await CreateTaskAsync("after nightly", s => s.BlockedBy = new List<string> { recurring.Id });

            // Simulate one fired occurrence completing while the task is still project-scoped.
            await _store.Project.InsertRunAsync(new TaskRun { TaskId = recurring.Id, ScheduledFor = DateTime.UtcNow });
            await _store.CompleteAsync(recurring.Id, success: true);

            (await _store.IsBlockedAsync(dependent.Id)).Should().BeFalse();

            await _store.UpdateAsync(recurring.Id, new TaskUpdateSpec { Scope = TaskScopes.Global });

            // The completed run must have moved with the task, or the dependent
            // becomes re-blocked because its history is now invisible.
            (await _store.Project.GetRunsAsync(recurring.Id)).Should().BeEmpty();
            var migratedRuns = await _store.Global.GetRunsAsync(recurring.Id);
            migratedRuns.Should().ContainSingle().Which.Outcome.Should().NotBeNull();
            (await _store.IsBlockedAsync(dependent.Id)).Should().BeFalse();
        }

        [Fact]
        public async Task TryEnqueueWakeAsync_DeduplicatesByKey()
        {
            var first = await _store.Project.TryEnqueueWakeAsync(new WakeItem { Kind = "test", Prompt = "p", DedupeKey = "dup:1" });
            var second = await _store.Project.TryEnqueueWakeAsync(new WakeItem { Kind = "test", Prompt = "p", DedupeKey = "dup:1" });

            first.Should().BeTrue();
            second.Should().BeFalse();
            (await _store.Project.GetPendingWakesAsync()).Should().HaveCount(1);
        }

        [Fact]
        public async Task MarkWaiterDelivered_ClaimsExactlyOnce_AndResetMakesItPendingAgain()
        {
            var waiter = await _store.Project.InsertWaiterAsync(new TaskWaiter { WaitTargetId = "task_x" });

            (await _store.Project.MarkWaiterDeliveredAsync(waiter.Id)).Should().BeTrue();
            (await _store.Project.MarkWaiterDeliveredAsync(waiter.Id)).Should().BeFalse();
            (await _store.Project.GetPendingWaitersAsync("task_x")).Should().BeEmpty();

            await _store.Project.ResetWaiterDeliveryAsync(waiter.Id);

            var pending = await _store.Project.GetPendingWaitersAsync("task_x");
            pending.Should().ContainSingle();
            pending[0].DeliveryAttempts.Should().Be(1);
            (await _store.Project.MarkWaiterDeliveredAsync(waiter.Id)).Should().BeTrue();
        }
    }
}
