using System;
using System.Threading;
using System.Threading.Tasks;
using Saturn.Config;

namespace Saturn.Core.Tasks
{
    public sealed class TaskSchedulerService : IDisposable
    {
        private readonly TaskCoordinator _coordinator;
        private readonly TaskSystemSettings _settings;
        private Timer? _timer;
        private int _sweeping;

        public TaskSchedulerService(TaskCoordinator coordinator, TaskSystemSettings settings)
        {
            _coordinator = coordinator;
            _settings = settings;
        }

        public void Start()
        {
            var interval = TimeSpan.FromSeconds(Math.Max(5, _settings.SchedulerIntervalSeconds));
            _timer = new Timer(_ => _ = SweepAsync(), null, interval, interval);
        }

        private async Task SweepAsync()
        {
            if (Interlocked.Exchange(ref _sweeping, 1) == 1)
            {
                return;
            }

            try
            {
                await RunStep(_coordinator.ProcessDueRecurrencesAsync);
                await RunStep(_coordinator.ProcessReadyTasksAsync);
                await RunStep(_coordinator.PumpWakeQueueAsync);
            }
            finally
            {
                Interlocked.Exchange(ref _sweeping, 0);
            }
        }

        private static async Task RunStep(Func<Task> step)
        {
            try
            {
                await step();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Scheduler step failed: {ex.Message}");
            }
        }

        public void Dispose()
        {
            _timer?.Dispose();
        }
    }
}
