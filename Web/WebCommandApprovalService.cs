using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Saturn.Tools.Core;

namespace Saturn.Web
{
    public class PendingCommandApproval
    {
        public string Id { get; init; } = Guid.NewGuid().ToString("N")[..12];
        public string Command { get; init; } = "";
        public string WorkingDirectory { get; init; } = "";
        public DateTime RequestedAt { get; init; } = DateTime.UtcNow;
    }

    public class WebCommandApprovalService : ICommandApprovalService
    {
        private static readonly TimeSpan ApprovalTimeout = TimeSpan.FromMinutes(10);

        private readonly EventHub _hub;
        private readonly ConcurrentDictionary<string, (PendingCommandApproval Request, TaskCompletionSource<bool> Completion)> _pending = new();

        public WebCommandApprovalService(EventHub hub)
        {
            _hub = hub;
        }

        public List<PendingCommandApproval> GetPending()
        {
            return _pending.Values.Select(p => p.Request).OrderBy(r => r.RequestedAt).ToList();
        }

        public async Task<bool> RequestApprovalAsync(string command, string workingDirectory)
        {
            var request = new PendingCommandApproval
            {
                Command = command,
                WorkingDirectory = workingDirectory
            };
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[request.Id] = (request, completion);

            _hub.Publish("approval.requested", request);

            using var cts = new CancellationTokenSource(ApprovalTimeout);
            await using var registration = cts.Token.Register(() =>
            {
                if (_pending.TryRemove(request.Id, out var entry))
                {
                    entry.Completion.TrySetResult(false);
                    _hub.Publish("approval.resolved", new { id = request.Id, approved = false, reason = "timeout" });
                }
            });

            return await completion.Task;
        }

        public bool Resolve(string id, bool approved)
        {
            if (_pending.TryRemove(id, out var entry))
            {
                entry.Completion.TrySetResult(approved);
                _hub.Publish("approval.resolved", new { id, approved, reason = "user" });
                return true;
            }
            return false;
        }
    }
}
