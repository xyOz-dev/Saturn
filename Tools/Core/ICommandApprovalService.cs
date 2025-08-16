using System.Threading.Tasks;

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
            
            var tcs = new TaskCompletionSource<bool>();
            
            Terminal.Gui.Application.MainLoop.Invoke(() =>
            {
                using (var dialog = new UI.Dialogs.CommandApprovalDialog(command, workingDirectory))
                {
                    Terminal.Gui.Application.Run(dialog);
                    tcs.SetResult(dialog.Approved);
                }
            });
            
            return await tcs.Task;
        }
    }
}