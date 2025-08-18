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
        
        public override string GetDisplaySummary(Dictionary<string, object> parameters)
        {
            var taskIdsObj = GetParameter<object?>(parameters, "task_ids", null);
            var timeout = GetParameter<int>(parameters, "timeout_seconds", 30);
            
            string agentInfo = "agents";
            if (taskIdsObj is List<object> objList && objList.Count > 0)
            {
                agentInfo = objList.Count == 1 ? "1 agent" : $"{objList.Count} agents";
            }
            
            return $"Waiting for {agentInfo} (timeout: {timeout}s)";
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
                
                var startTime = DateTime.Now;
                var results = await AgentManager.Instance.WaitForAllTasks(taskIds, timeoutMs);
                var elapsedMs = (DateTime.Now - startTime).TotalMilliseconds;
                
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
                
                if (elapsedMs < 200 && results.Count == taskIds.Count)
                {
                    formatted += "(All tasks were already complete - returned immediately)\n";
                }
                else if (elapsedMs < 1000)
                {
                    formatted += $"(Retrieved in {elapsedMs:F0}ms)\n";
                }
                
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
                    if (elapsedMs >= timeoutMs - 100)
                    {
                        formatted += $" (Timeout reached after {timeoutSeconds}s)";
                    }
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