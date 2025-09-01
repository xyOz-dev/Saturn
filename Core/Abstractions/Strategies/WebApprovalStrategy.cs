using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Saturn.Core.Abstractions.Strategies
{
    public class WebApprovalStrategy : IApprovalStrategy
    {
        private ApprovalConfiguration _configuration = new();
        private readonly ConcurrentDictionary<string, PendingApproval> _pendingApprovals = new();
        
        private record PendingApproval(TaskCompletionSource<ApprovalResult> TaskCompletionSource, ApprovalRequest Request);
        
        public string PlatformName => "Web";
        
        public event Action<ApprovalRequest>? OnApprovalRequested;
        
        public async Task<ApprovalResult> RequestApprovalAsync(ApprovalRequest request)
        {
            var tcs = new TaskCompletionSource<ApprovalResult>();
            var pendingApproval = new PendingApproval(tcs, request);
            _pendingApprovals[request.Id] = pendingApproval;
            
            OnApprovalRequested?.Invoke(request);
            
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(request.TimeoutSeconds));
            
            CancellationTokenRegistration registration = default;
            try
            {
                registration = cts.Token.Register(() =>
                {
                    if (_pendingApprovals.TryRemove(request.Id, out var pendingApproval))
                    {
                        pendingApproval.TaskCompletionSource.TrySetResult(new ApprovalResult
                        {
                            Approved = false,
                            Reason = "Request timed out",
                            RequestedAt = pendingApproval.Request.RequestedAt
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
            finally
            {
                registration.Dispose();
                _pendingApprovals.TryRemove(request.Id, out _);
            }
        }
        
        public void ProcessApprovalResponse(string requestId, bool approved, string? approvedBy = null, string? reason = null)
        {
            if (_pendingApprovals.TryRemove(requestId, out var pendingApproval))
            {
                pendingApproval.TaskCompletionSource.TrySetResult(new ApprovalResult
                {
                    Approved = approved,
                    Reason = reason ?? (approved ? "Approved via web" : "Denied via web"),
                    ApprovedBy = approvedBy ?? "Web User",
                    RequestedAt = pendingApproval.Request.RequestedAt
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