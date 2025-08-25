using System;
using System.Threading.Tasks;

namespace Saturn.Core.Abstractions.Strategies
{
    public class AutoApprovalStrategy : IApprovalStrategy
    {
        private ApprovalConfiguration _configuration = new();
        private readonly bool _approveAll;
        
        public string PlatformName => "Auto";
        
        public AutoApprovalStrategy(bool approveAll = false)
        {
            _approveAll = approveAll;
        }
        
        public Task<ApprovalResult> RequestApprovalAsync(ApprovalRequest request)
        {
            var shouldApprove = _approveAll || 
                               !_configuration.RequireApprovalByDefault ||
                               _configuration.AutoApprovedCommands.Any(cmd => 
                                   request.Command.StartsWith(cmd, StringComparison.OrdinalIgnoreCase));
            
            return Task.FromResult(new ApprovalResult
            {
                Approved = shouldApprove,
                Reason = shouldApprove ? "Auto-approved" : "Auto-denied based on configuration",
                ApprovedBy = "System",
                RequestedAt = request.RequestedAt
            });
        }
        
        public Task<bool> IsApprovalRequiredAsync(string command, string context)
        {
            if (_approveAll)
                return Task.FromResult(false);
                
            return Task.FromResult(_configuration.RequireApprovalByDefault);
        }
        
        public void Configure(ApprovalConfiguration configuration)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }
    }
}