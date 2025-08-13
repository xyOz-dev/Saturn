using System.Collections.Generic;
using System.Threading.Tasks;

namespace Saturn.Tools.Core
{
    public interface ITool
    {
        string Name { get; }
        string Description { get; }
        Dictionary<string, object> GetParameters();
        Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters);
        string GetDisplaySummary(Dictionary<string, object> parameters);
    }
    
    public class ToolResult
    {
        public bool Success { get; set; }
        public string FormattedOutput { get; set; }
        public object RawData { get; set; }
        public string Error { get; set; }
    }
}