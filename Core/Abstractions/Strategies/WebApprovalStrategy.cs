using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace Saturn.Core.Abstractions.Strategies
{
    public class WebApprovalStrategy : IApprovalStrategy
    {
        private ApprovalConfiguration _configuration = new();
        private readonly ConcurrentDictionary<string, TaskCompletionSource<ApprovalResult>> _pendingApprovals = new();
        
        public string PlatformName => "Web";
        
        public event Action<ApprovalRequest>? OnApprovalRequested;
        
        public async Task<ApprovalResult> RequestApprovalAsync(ApprovalRequest request)
        {
            var tcs = new TaskCompletionSource<ApprovalResult>();
            _pendingApprovals[request.Id] = tcs;
            
            OnApprovalRequested?.Invoke(request);
            
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(request.TimeoutSeconds));
            
            try
            {
                cts.Token.Register(() =>
                {
                    if (_pendingApprovals.TryRemove(request.Id, out var timeoutTcs))
                    {
                        timeoutTcs.TrySetResult(new ApprovalResult
                        {
                            Approved = false,
                            Reason = "Request timed out",
                            RequestedAt = request.RequestedAt
                        });
                    }
                });
                
                return await tcs.Task;
            }
            catch (TaskCanceledException)
            {
                return new ApprovalResult
                {
                    Approved = false,
                    Reason = "Request cancelled",
                    RequestedAt = request.RequestedAt
                };
            }
        }
        
        public void ProcessApprovalResponse(string requestId, bool approved, string? approvedBy = null, string? reason = null)
        {
            if (_pendingApprovals.TryRemove(requestId, out var tcs))
            {
                tcs.TrySetResult(new ApprovalResult
                {
                    Approved = approved,
                    Reason = reason ?? (approved ? "Approved via web" : "Denied via web"),
                    ApprovedBy = approvedBy ?? "Web User",
                    RequestedAt = DateTime.UtcNow
                });
            }
        }
        
        public Task<bool> IsApprovalRequiredAsync(string command, string context)
        {
            if (_configuration.AutoApprovedCommands.Any(cmd => 
                command.StartsWith(cmd, StringComparison.OrdinalIgnoreCase)))
            {
                return Task.FromResult(false);
            }
            
            if (_configuration.AlwaysRequireApprovalCommands.Any(cmd => 
                command.Contains(cmd, StringComparison.OrdinalIgnoreCase)))
            {
                return Task.FromResult(true);
            }
            
            return Task.FromResult(_configuration.RequireApprovalByDefault);
        }
        
        public void Configure(ApprovalConfiguration configuration)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }
    }
}