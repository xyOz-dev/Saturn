using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Saturn.Tools.Core
{
    public class ToolRegistry
    {
        private readonly Dictionary<string, ITool> _tools = new Dictionary<string, ITool>(StringComparer.OrdinalIgnoreCase);
        private static readonly Lazy<ToolRegistry> _instance = new Lazy<ToolRegistry>(() => new ToolRegistry());
        
        public static ToolRegistry Instance => _instance.Value;
        
        private ToolRegistry()
        {
            AutoRegisterTools();
        }
        
        private void AutoRegisterTools()
        {
            var toolType = typeof(ITool);
            var assembly = Assembly.GetExecutingAssembly();
            
            var toolTypes = assembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && toolType.IsAssignableFrom(t));
            
            foreach (var type in toolTypes)
            {
                try
                {
                    var tool = (ITool)Activator.CreateInstance(type);
                    Register(tool);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to auto-register tool {type.Name}: {ex.Message}");
                }
            }
        }
        
        public void Register(ITool tool)
        {
            if (tool == null)
            {
                throw new ArgumentNullException(nameof(tool));
            }
            
            _tools[tool.Name] = tool;
        }
        
        public void Register<T>() where T : ITool, new()
        {
            Register(new T());
        }
        
        public ITool Get(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }
            
            _tools.TryGetValue(name, out var tool);
            return tool;
        }
        
        public T Get<T>(string name) where T : class, ITool
        {
            return Get(name) as T;
        }
        
        public bool Contains(string name)
        {
            return !string.IsNullOrEmpty(name) && _tools.ContainsKey(name);
        }
        
        public IEnumerable<ITool> GetAll()
        {
            return _tools.Values;
        }
        
        public IEnumerable<string> GetAllNames()
        {
            return _tools.Keys;
        }
        
        public List<OpenRouterSharp.Models.Requests.Tool> GetOpenRouterTools(params string[] toolNames)
        {
            var tools = new List<OpenRouterSharp.Models.Requests.Tool>();
            
            var selectedTools = toolNames?.Length > 0 
                ? toolNames.Select(Get).Where(t => t != null)
                : GetAll();
            
            foreach (var tool in selectedTools)
            {
                tools.Add(new OpenRouterSharp.Models.Requests.Tool
                {
                    Type = "function",
                    Function = new OpenRouterSharp.Models.Requests.Function
                    {
                        Name = tool.Name,
                        Description = tool.Description,
                        Parameters = tool.GetParameters()
                    }
                });
            }
            
            return tools;
        }
        
        public void Clear()
        {
            _tools.Clear();
        }
    }
}