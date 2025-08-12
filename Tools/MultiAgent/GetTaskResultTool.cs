using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Saturn.Tools.Core;
using Saturn.Agents.MultiAgent;

namespace Saturn.Tools.MultiAgent
{
    public class GetTaskResultTool : ToolBase
    {
        public override string Name => "get_task_result";
        
        public override string Description => "Get the result of a completed agent task without waiting";
        
        protected override Dictionary<string, object> GetParameterProperties()
        {
            return new Dictionary<string, object>
            {
                ["task_id"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["description"] = "The task ID to retrieve results for"
                }
            };
        }
        
        protected override string[] GetRequiredParameters()
        {
            return new[] { "task_id" };
        }
        
        public override Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters)
        {
            try
            {
                var taskId = parameters["task_id"].ToString()!;
                var result = AgentManager.Instance.GetTaskResult(taskId);
                
                if (result == null)
                {
                    return Task.FromResult(CreateErrorResult($"Task {taskId} not found or still running"));
                }
                
                return Task.FromResult(CreateSuccessResult(
                    new Dictionary<string, object>
                    {
                        ["task_id"] = result.TaskId,
                        ["agent_id"] = result.AgentId,
                        ["agent_name"] = result.AgentName,
                        ["success"] = result.Success,
                        ["result"] = result.Result,
                        ["completed_at"] = result.CompletedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                        ["duration_seconds"] = result.Duration.TotalSeconds
                    },
                    $"{result.AgentName} completed task {result.TaskId}:\n" +
                        $"Status: {(result.Success ? "Success" : "Failed")}\n" +
                        $"Duration: {result.Duration.TotalSeconds:F1}s\n" +
                        $"Result: {result.Result}"
                ));
            }
            catch (Exception ex)
            {
                return Task.FromResult(CreateErrorResult($"Failed to get task result: {ex.Message}"));
            }
        }
    }
}