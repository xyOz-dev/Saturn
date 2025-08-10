using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Saturn.Commands.Core
{
    public abstract class CommandBase : ICommand
    {
        public abstract string Name { get; }
        public abstract string Description { get; }
        public virtual string[] Aliases => Array.Empty<string>();
        public virtual CommandCategory Category => CommandCategory.Custom;
        public virtual bool RequiresConfirmation => false;
        
        public abstract Task<CommandResult> ExecuteAsync(string[] args);
        
        public virtual bool CanExecute(string[] args)
        {
            return true;
        }
        
        public virtual string GetUsage()
        {
            var aliasText = Aliases.Length > 0 
                ? $" (aliases: {string.Join(", ", Aliases)})" 
                : "";
            return $"/{Name}{aliasText} - {Description}";
        }
        
        protected T ParseArgument<T>(string[] args, int index, T defaultValue = default)
        {
            if (args == null || index >= args.Length)
            {
                return defaultValue;
            }
            
            var value = args[index];
            
            try
            {
                if (typeof(T) == typeof(string))
                {
                    return (T)(object)value;
                }
                
                if (typeof(T) == typeof(int))
                {
                    return (T)(object)int.Parse(value);
                }
                
                if (typeof(T) == typeof(bool))
                {
                    return (T)(object)bool.Parse(value);
                }
                
                if (typeof(T) == typeof(double))
                {
                    return (T)(object)double.Parse(value);
                }
                
                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch
            {
                return defaultValue;
            }
        }
        
        protected string GetArgumentsAsString(string[] args, int startIndex = 0)
        {
            if (args == null || startIndex >= args.Length)
            {
                return string.Empty;
            }
            
            return string.Join(" ", args.Skip(startIndex));
        }
        
        protected Dictionary<string, string> ParseNamedArguments(string[] args)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            
            if (args == null)
                return result;
            
            for (int i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                
                if (arg.StartsWith("--"))
                {
                    var key = arg.Substring(2);
                    var equalIndex = key.IndexOf('=');
                    
                    if (equalIndex > 0)
                    {
                        var name = key.Substring(0, equalIndex);
                        var value = key.Substring(equalIndex + 1);
                        result[name] = value;
                    }
                    else if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                    {
                        result[key] = args[++i];
                    }
                    else
                    {
                        result[key] = "true";
                    }
                }
                else if (arg.StartsWith("-") && arg.Length == 2)
                {
                    var key = arg.Substring(1);
                    
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                    {
                        result[key] = args[++i];
                    }
                    else
                    {
                        result[key] = "true";
                    }
                }
            }
            
            return result;
        }
        
        protected List<string> GetPositionalArguments(string[] args)
        {
            var result = new List<string>();
            
            if (args == null)
                return result;
            
            for (int i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                
                if (arg.StartsWith("-"))
                {
                    if (arg.StartsWith("--") && arg.Contains("="))
                    {
                        continue;
                    }
                    
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                    {
                        i++;
                    }
                }
                else
                {
                    result.Add(arg);
                }
            }
            
            return result;
        }
    }
}