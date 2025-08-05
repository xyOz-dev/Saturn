using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace Saturn.Tools.Core
{
    public abstract class ToolBase : ITool
    {
        public abstract string Name { get; }
        public abstract string Description { get; }
        
        public virtual Dictionary<string, object> GetParameters()
        {
            var parameters = new Dictionary<string, object>
            {
                { "type", "object" },
                { "properties", GetParameterProperties() },
                { "required", GetRequiredParameters() }
            };
            return parameters;
        }
        
        protected abstract Dictionary<string, object> GetParameterProperties();
        protected abstract string[] GetRequiredParameters();
        
        public abstract Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters);
        
        protected T GetParameter<T>(Dictionary<string, object> parameters, string key, T defaultValue = default)
        {
            if (parameters.TryGetValue(key, out var value))
            {
                if (value is T typedValue)
                {
                    return typedValue;
                }
                
                try
                {
                    return (T)Convert.ChangeType(value, typeof(T));
                }
                catch
                {
                    return defaultValue;
                }
            }
            
            return defaultValue;
        }
        
        protected ToolResult CreateSuccessResult(object data, string formattedOutput = null)
        {
            return new ToolResult
            {
                Success = true,
                RawData = data,
                FormattedOutput = formattedOutput ?? JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true })
            };
        }
        
        protected ToolResult CreateErrorResult(string error)
        {
            return new ToolResult
            {
                Success = false,
                Error = error,
                FormattedOutput = $"Error: {error}"
            };
        }
    }
}