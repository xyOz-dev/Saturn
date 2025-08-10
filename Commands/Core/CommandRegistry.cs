using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace Saturn.Commands.Core
{
    public class CommandRegistry
    {
        private readonly Dictionary<string, ICommand> _commands = new Dictionary<string, ICommand>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ICommand> _aliases = new Dictionary<string, ICommand>(StringComparer.OrdinalIgnoreCase);
        private static readonly Lazy<CommandRegistry> _instance = new Lazy<CommandRegistry>(() => new CommandRegistry());
        
        public static CommandRegistry Instance => _instance.Value;
        
        private CommandRegistry()
        {
            AutoRegisterCommands();
        }
        
        private void AutoRegisterCommands()
        {
            var commandType = typeof(ICommand);
            var assembly = Assembly.GetExecutingAssembly();
            
            var commandTypes = assembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && commandType.IsAssignableFrom(t));
            
            foreach (var type in commandTypes)
            {
                try
                {
                    var command = (ICommand)Activator.CreateInstance(type);
                    Register(command);
                }
                catch (Exception ex)
                {
                    System.Console.WriteLine($"Failed to auto-register command {type.Name}: {ex.Message}");
                }
            }
        }
        
        public void Register(ICommand command)
        {
            if (command == null)
            {
                throw new ArgumentNullException(nameof(command));
            }
            
            _commands[command.Name] = command;
            
            if (command.Aliases != null)
            {
                foreach (var alias in command.Aliases)
                {
                    _aliases[alias] = command;
                }
            }
        }
        
        public void Register<T>() where T : ICommand, new()
        {
            Register(new T());
        }
        
        public ICommand Get(string nameOrAlias)
        {
            if (string.IsNullOrEmpty(nameOrAlias))
            {
                return null;
            }
            
            nameOrAlias = nameOrAlias.TrimStart('/');
            
            if (_commands.TryGetValue(nameOrAlias, out var command))
            {
                return command;
            }
            
            if (_aliases.TryGetValue(nameOrAlias, out command))
            {
                return command;
            }
            
            return null;
        }
        
        public T Get<T>(string nameOrAlias) where T : class, ICommand
        {
            return Get(nameOrAlias) as T;
        }
        
        public bool Contains(string nameOrAlias)
        {
            if (string.IsNullOrEmpty(nameOrAlias))
                return false;
                
            nameOrAlias = nameOrAlias.TrimStart('/');
            return _commands.ContainsKey(nameOrAlias) || _aliases.ContainsKey(nameOrAlias);
        }
        
        public IEnumerable<ICommand> GetAll()
        {
            return _commands.Values;
        }
        
        public IEnumerable<ICommand> GetByCategory(CommandCategory category)
        {
            return _commands.Values.Where(c => c.Category == category);
        }
        
        public IEnumerable<string> GetAllNames()
        {
            return _commands.Keys;
        }
        
        public IEnumerable<string> GetAllNamesAndAliases()
        {
            return _commands.Keys.Concat(_aliases.Keys).Distinct(StringComparer.OrdinalIgnoreCase);
        }
        
        public async Task<CommandResult> ExecuteAsync(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return CommandResult.ErrorResult("No command provided");
            }
            
            input = input.Trim();
            if (!input.StartsWith("/"))
            {
                return CommandResult.ErrorResult("Commands must start with '/'");
            }
            
            var parts = ParseCommandLine(input.Substring(1));
            if (parts.Length == 0)
            {
                return CommandResult.ErrorResult("Invalid command");
            }
            
            var commandName = parts[0];
            var args = parts.Skip(1).ToArray();
            
            var command = Get(commandName);
            if (command == null)
            {
                return CommandResult.ErrorResult($"Unknown command: /{commandName}");
            }
            
            if (!command.CanExecute(args))
            {
                return CommandResult.ErrorResult($"Cannot execute command: {command.GetUsage()}");
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
        
        public bool IsCommand(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return false;
                
            input = input.Trim();
            if (!input.StartsWith("/"))
                return false;
                
            var parts = input.Substring(1).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return false;
                
            return Contains(parts[0]);
        }
        
        private string[] ParseCommandLine(string commandLine)
        {
            var parts = new List<string>();
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
        
        public void Clear()
        {
            _commands.Clear();
            _aliases.Clear();
        }
    }
}