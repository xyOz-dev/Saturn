using System;
using System.Threading.Tasks;
using Saturn.UI.Dialogs;

namespace Saturn.Tools.Core
{
    public interface ICommandApprovalService
    {
        Task<bool> RequestApprovalAsync(string command, string workingDirectory);
    }
    
    public class CommandApprovalService : ICommandApprovalService
    {
        private readonly bool _requireApproval;
        
        public CommandApprovalService(bool requireApproval = true)
        {
            _requireApproval = requireApproval;
        }
        
        public async Task<bool> RequestApprovalAsync(string command, string workingDirectory)
        {
            if (!_requireApproval)
            {
                return true;
            }
            
            if (Terminal.Gui.Application.MainLoop == null)
            {
                return false;
            }
            
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            
            Terminal.Gui.Application.MainLoop.Invoke(() =>
            {
                try
                {
                    using (var dialog = new CommandApprovalDialog(command, workingDirectory))
                    {
                        Terminal.Gui.Application.Run(dialog);
                        tcs.SetResult(dialog.Approved);
                    }
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });
            
            return await tcs.Task;
        }
    }
}