using System;
using System.Threading.Tasks;
using Saturn.Console;

namespace Saturn.Commands.Core
{
    public class CommandExecutor
    {
        private readonly CommandRegistry _registry;
        private readonly Theme _theme;
        
        public CommandExecutor(Theme theme = null)
        {
            _registry = CommandRegistry.Instance;
            _theme = theme ?? Theme.Default;
        }
        
        public async Task<CommandResult> ExecuteAsync(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return CommandResult.ErrorResult("No input provided");
            }
            
            input = input.Trim();
            
            if (!_registry.IsCommand(input))
            {
                return null;
            }
            
            HistoryCommand.AddToHistory(input);
            
            var parts = ParseCommandLine(input.Substring(1));
            if (parts.Length == 0)
            {
                return CommandResult.ErrorResult("Invalid command");
            }
            
            var commandName = parts[0];
            var args = parts.Length > 1 ? parts[1..] : Array.Empty<string>();
            
            var command = _registry.Get(commandName);
            if (command == null)
            {
                return CommandResult.ErrorResult($"Unknown command: /{commandName}");
            }
            
            if (command.RequiresConfirmation)
            {
                if (!await ConfirmExecutionAsync(command))
                {
                    return CommandResult.SuccessResult("Command cancelled");
                }
            }
            
            try
            {
                return await command.ExecuteAsync(args);
            }
            catch (Exception ex)
            {
                return CommandResult.ErrorResult($"Command failed: {ex.Message}");
            }
        }
        
        private async Task<bool> ConfirmExecutionAsync(ICommand command)
        {
            await Task.Yield();
            
            System.Console.WriteLine();
            System.Console.Write(_theme.Accent.Fg());
            System.Console.Write($"⚠️  Execute command '/{command.Name}'? This action requires confirmation. [y/N]: ");
            System.Console.Write(Ansi.Reset);
            
            var response = System.Console.ReadLine();
            
            return !string.IsNullOrWhiteSpace(response) && 
                   (response.Equals("y", StringComparison.OrdinalIgnoreCase) || 
                    response.Equals("yes", StringComparison.OrdinalIgnoreCase));
        }
        
        private string[] ParseCommandLine(string commandLine)
        {
            var parts = new System.Collections.Generic.List<string>();
            var current = "";
            var inQuotes = false;
            var escapeNext = false;
            
            foreach (char c in commandLine)
            {
                if (escapeNext)
                {
                    current += c;
                    escapeNext = false;
                    continue;
                }
                
                if (c == '\\')
                {
                    escapeNext = true;
                    continue;
                }
                
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                    continue;
                }
                
                if (c == ' ' && !inQuotes)
                {
                    if (!string.IsNullOrEmpty(current))
                    {
                        parts.Add(current);
                        current = "";
                    }
                    continue;
                }
                
                current += c;
            }
            
            if (!string.IsNullOrEmpty(current))
            {
                parts.Add(current);
            }
            
            return parts.ToArray();
        }
    }
}