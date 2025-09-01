using System;
using System.Linq;
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
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            var command = request.Command ?? string.Empty;
            var autoApprovedCommands = _configuration.AutoApprovedCommands ?? Enumerable.Empty<string>();
            
            var shouldApprove = _approveAll || 
                               !_configuration.RequireApprovalByDefault ||
                               autoApprovedCommands.Any(cmd => 
                                   !string.IsNullOrEmpty(cmd) && command.StartsWith(cmd, StringComparison.OrdinalIgnoreCase));
            
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