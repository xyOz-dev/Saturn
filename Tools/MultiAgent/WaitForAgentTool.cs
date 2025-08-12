using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Saturn.Tools.Core;
using Saturn.Agents.MultiAgent;

namespace Saturn.Tools.MultiAgent
{
    public class WaitForAgentTool : ToolBase
    {
        public override string Name => "wait_for_agent";
        
        public override string Description => "Wait for one or more agent tasks to complete and retrieve their results";
        
        protected override Dictionary<string, object> GetParameterProperties()
        {
            return new Dictionary<string, object>
            {
                ["task_ids"] = new Dictionary<string, object>
                {
                    ["type"] = "array",
                    ["description"] = "List of task IDs to wait for",
                    ["items"] = new Dictionary<string, object> { ["type"] = "string" }
                },
                ["timeout_seconds"] = new Dictionary<string, object>
                {
                    ["type"] = "number",
                    ["description"] = "Maximum time to wait in seconds (default: 30)"
                }
            };
        }
        
        protected override string[] GetRequiredParameters()
        {
            return new[] { "task_ids" };
        }
        
        public override async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters)
        {
            try
            {
                var taskIdsObj = parameters["task_ids"];
                List<string> taskIds;
                
                if (taskIdsObj is List<object> objList)
                {
                    taskIds = objList.Select(o => o.ToString()!).ToList();
                }
                else if (taskIdsObj is string singleId)
                {
                    taskIds = new List<string> { singleId };
                }
                else
                {
                    taskIds = new List<string>();
                }
                
                var timeoutSeconds = parameters.ContainsKey("timeout_seconds") ? 
                    Convert.ToInt32(parameters["timeout_seconds"]) : 30;
                var timeoutMs = timeoutSeconds * 1000;
                
                var results = await AgentManager.Instance.WaitForAllTasks(taskIds, timeoutMs);
                
                if (!results.Any())
                {
                    return CreateErrorResult($"Timeout: No tasks completed within {timeoutSeconds} seconds");
                }
                
                var output = results.Select(r => new Dictionary<string, object>
                {
                    ["task_id"] = r.TaskId,
                    ["agent_id"] = r.AgentId,
                    ["agent_name"] = r.AgentName,
                    ["success"] = r.Success,
                    ["result"] = r.Result,
                    ["duration_seconds"] = r.Duration.TotalSeconds
                }).ToList();
                
                var formatted = "Task Results:\n";
                foreach (var result in results)
                {
                    formatted += $"\n{result.AgentName} (Task: {result.TaskId}):\n";
                    formatted += $"  Status: {(result.Success ? "Success" : "Failed")}\n";
                    formatted += $"  Duration: {result.Duration.TotalSeconds:F1}s\n";
                    formatted += $"  Result: {result.Result}\n";
                }
                
                var pendingTasks = taskIds.Except(results.Select(r => r.TaskId)).ToList();
                if (pendingTasks.Any())
                {
                    formatted += $"\nTasks still pending: {string.Join(", ", pendingTasks)}";
                }
                
                return CreateSuccessResult(
                    new Dictionary<string, object>
                    {
                        ["completed_tasks"] = output,
                        ["pending_tasks"] = pendingTasks,
                        ["completion_rate"] = $"{results.Count}/{taskIds.Count}"
                    },
                    formatted
                );
            }
            catch (Exception ex)
            {
                return CreateErrorResult($"Failed to wait for tasks: {ex.Message}");
            }
        }
    }
}