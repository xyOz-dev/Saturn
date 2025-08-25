using System;
using System.Threading.Tasks;
using Saturn.UI.Dialogs;

namespace Saturn.Core.Abstractions.Strategies
{
    public class UIApprovalStrategy : IApprovalStrategy
    {
        private ApprovalConfiguration _configuration = new();
        private readonly Func<CommandApprovalDialog>? _dialogFactory;
        
        public string PlatformName => "UI";
        
        public UIApprovalStrategy(Func<CommandApprovalDialog>? dialogFactory = null)
        {
            _dialogFactory = dialogFactory;
        }
        
        public async Task<ApprovalResult> RequestApprovalAsync(ApprovalRequest request)
        {
            if (_dialogFactory == null)
            {
                return new ApprovalResult
                {
                    Approved = false,
                    Reason = "No UI dialog factory configured",
                    RequestedAt = request.RequestedAt
                };
            }
            
            // Create a new dialog with command and description
            var dialog = new CommandApprovalDialog(request.Command, request.Description);
            
            // Run the dialog and get the result
            Terminal.Gui.Application.Run(dialog);
            
            return new ApprovalResult
            {
                Approved = dialog.Approved,
                Reason = dialog.Approved ? "User approved" : "User denied",
                ApprovedBy = "User",
                RequestedAt = request.RequestedAt
            };
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