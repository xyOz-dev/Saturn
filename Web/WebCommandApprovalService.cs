using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Saturn.Config;

namespace Saturn.Web
{
    public class PendingApprovalItem
    {
        public string Id { get; init; } = Guid.NewGuid().ToString("N")[..12];
        public string Type { get; init; } = "command";
        public string Title { get; init; } = "";
        public string? Detail { get; init; }
        public string? Command { get; init; }
        public string? WorkingDirectory { get; init; }
        public string? TaskId { get; init; }
        public string? AgentName { get; init; }
        public DateTime RequestedAt { get; init; } = DateTime.UtcNow;
    }

    // User-facing approval queue (web UI Approvals tab): blocking command
    // approvals and non-blocking decision requests (e.g. task claims).
    public class WebCommandApprovalService
    {
        private readonly EventHub _hub;
        private readonly TaskSystemSettings _settings;
        private readonly ConcurrentDictionary<string, (PendingApprovalItem Item, TaskCompletionSource<bool>? Completion, Action<bool>? OnResolved)> _pending = new();

        public WebCommandApprovalService(EventHub hub, TaskSystemSettings settings)
        {
            _hub = hub;
            _settings = settings;
        }

        public List<PendingApprovalItem> GetPending()
        {
            return _pending.Values.Select(p => p.Item).OrderBy(i => i.RequestedAt).ToList();
        }

        public async Task<bool> RequestCommandApprovalAsync(string command, string workingDirectory, string? agentName, string? detail)
        {
            var item = new PendingApprovalItem
            {
                Type = "command",
                Title = agentName != null ? $"Shell command from {agentName}" : "Shell command",
                Detail = detail,
                Command = command,
                WorkingDirectory = workingDirectory,
                AgentName = agentName
            };
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[item.Id] = (item, completion, null);
            _hub.Publish("approval.requested", item);

            var timeoutMinutes = _settings.ApprovalTimeoutMinutes;
            if (timeoutMinutes <= 0)
            {
                // Long-horizon mode: wait for the user indefinitely.
                return await completion.Task;
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(timeoutMinutes));
            await using var registration = cts.Token.Register(() =>
            {
                if (_pending.TryRemove(item.Id, out var entry))
                {
                    entry.Completion?.TrySetResult(false);
                    _hub.Publish("approval.resolved", new { id = item.Id, approved = false, resolvedBy = "timeout" });
                }
            });
            return await completion.Task;
        }

        public string RequestDecision(PendingApprovalItem item, Action<bool> onResolved)
        {
            _pending[item.Id] = (item, null, onResolved);
            _hub.Publish("approval.requested", item);
            return item.Id;
        }

        public bool Resolve(string id, bool approved)
        {
            if (!_pending.TryRemove(id, out var entry))
            {
                return false;
            }

            entry.Completion?.TrySetResult(approved);
            try
            {
                entry.OnResolved?.Invoke(approved);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Approval callback error: {ex.Message}");
            }
            _hub.Publish("approval.resolved", new { id, approved, resolvedBy = "user" });
            return true;
        }
    }
}
