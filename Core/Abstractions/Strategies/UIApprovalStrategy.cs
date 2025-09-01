using System;
using System.Linq;
using System.Threading.Tasks;
using Saturn.UI.Dialogs;

namespace Saturn.Core.Abstractions.Strategies
{
    public class UIApprovalStrategy : IApprovalStrategy
    {
        private ApprovalConfiguration _configuration = new();
        private readonly Func<ApprovalRequest, CommandApprovalDialog>? _dialogFactory;
        
        public string PlatformName => "UI";
        
        public UIApprovalStrategy(Func<ApprovalRequest, CommandApprovalDialog>? dialogFactory = null)
        {
            _dialogFactory = dialogFactory;
        }
        
        public Task<ApprovalResult> RequestApprovalAsync(ApprovalRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            // Create dialog using factory if present, otherwise create default
            var dialog = _dialogFactory?.Invoke(request) ?? 
                        new CommandApprovalDialog(request.Command ?? string.Empty, request.Description ?? string.Empty);
            
            // Run the dialog and get the result
            Terminal.Gui.Application.Run(dialog);
            
            var result = new ApprovalResult
            {
                Approved = dialog.Approved,
                Reason = dialog.Approved ? "User approved" : "User denied",
                ApprovedBy = "User",
                RequestedAt = request.RequestedAt
            };
            
            return Task.FromResult(result);
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