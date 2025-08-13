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
        
        public virtual string GetDisplaySummary(Dictionary<string, object> parameters)
        {
            return $"Executing {Name}";
        }
        
        protected string TruncateString(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;
            
            if (text.Length <= maxLength)
                return text;
            
            return text.Substring(0, maxLength - 3) + "...";
        }
        
        protected string FormatPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return string.Empty;
            
            const int maxLength = 50;
            if (path.Length <= maxLength)
                return path;
            
            var fileName = System.IO.Path.GetFileName(path);
            if (fileName.Length >= maxLength - 10)
            {
                return "..." + path.Substring(path.Length - (maxLength - 3));
            }
            
            var directory = System.IO.Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(directory))
                return fileName;
            
            var availableForDir = maxLength - fileName.Length - 4;
            if (availableForDir <= 0)
                return ".../" + fileName;
            
            if (directory.Length <= availableForDir)
                return path;
            
            return "..." + directory.Substring(directory.Length - availableForDir) + "/" + fileName;
        }
        
        protected string FormatByteSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double size = bytes;
            int order = 0;
            
            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size /= 1024;
            }
            
            return $"{size:0.##} {sizes[order]}";
        }
    }
}