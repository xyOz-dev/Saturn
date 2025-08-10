using System.Collections.Generic;
using System.Threading.Tasks;

namespace Saturn.Commands.Core
{
    public interface ICommand
    {
        string Name { get; }
        string Description { get; }
        string[] Aliases { get; }
        CommandCategory Category { get; }
        bool RequiresConfirmation { get; }
        
        Task<CommandResult> ExecuteAsync(string[] args);
        bool CanExecute(string[] args);
        string GetUsage();
    }
    
    public class CommandResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public object Data { get; set; }
        public CommandAction Action { get; set; }
        
        public static CommandResult SuccessResult(string message = null, object data = null)
        {
            return new CommandResult 
            { 
                Success = true, 
                Message = message,
                Data = data,
                Action = CommandAction.None
            };
        }
        
        public static CommandResult ErrorResult(string error)
        {
            return new CommandResult 
            { 
                Success = false, 
                Message = error,
                Action = CommandAction.None
            };
        }
        
        public static CommandResult ExitResult(string message = null)
        {
            return new CommandResult
            {
                Success = true,
                Message = message,
                Action = CommandAction.Exit
            };
        }
    }
    
    public enum CommandAction
    {
        None,
        Exit,
        Clear,
        Reload,
        SwitchMode
    }
    
    public enum CommandCategory
    {
        System,
        Navigation,
        Configuration,
        Development,
        Information,
        Custom
    }
}