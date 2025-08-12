using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Saturn.Tools.Core;
using Saturn.Agents.MultiAgent;

namespace Saturn.Tools.MultiAgent
{
    public class HandOffToAgentTool : ToolBase
    {
        public override string Name => "hand_off_to_agent";
        
        public override string Description => "Hand off a task to a sub-agent for parallel execution. Returns a task ID to track progress.";
        
        protected override Dictionary<string, object> GetParameterProperties()
        {
            return new Dictionary<string, object>
            {
                ["agent_id"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["description"] = "The ID of the agent to hand off the task to"
                },
                ["task"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["description"] = "The task description for the agent to execute"
                },
                ["context"] = new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["description"] = "Optional context information for the task"
                }
            };
        }
        
        protected override string[] GetRequiredParameters()
        {
            return new[] { "agent_id", "task" };
        }
        
        public override async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters)
        {
            try
            {
                var agentId = parameters["agent_id"].ToString()!;
                var task = parameters["task"].ToString()!;
                var context = parameters.ContainsKey("context") ? 
                    parameters["context"] as Dictionary<string, object> : null;
                
                var taskId = await AgentManager.Instance.HandOffTask(agentId, task, context);
                
                return CreateSuccessResult(
                    new Dictionary<string, object>
                    {
                        ["task_id"] = taskId,
                        ["agent_id"] = agentId,
                        ["status"] = "Task handed off successfully"
                    },
                    $"Task handed off to agent {agentId}. Task ID: {taskId}"
                );
            }
            catch (Exception ex)
            {
                return CreateErrorResult($"Failed to hand off task: {ex.Message}");
            }
        }
    }
}