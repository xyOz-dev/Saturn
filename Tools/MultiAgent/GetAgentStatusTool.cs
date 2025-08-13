using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Saturn.Tools.Core;
using Saturn.Agents.MultiAgent;

namespace Saturn.Tools.MultiAgent
{
    public class GetAgentStatusTool : ToolBase
    {
        public override string Name => "get_agent_status";
        
        public override string Description => "Get the current status of one or all sub-agents";
        
        protected override Dictionary<string, object> GetParameterProperties()
        {
            return new Dictionary<string, object>
            {
                ["agent_id"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["description"] = "The ID of a specific agent, or 'all' for all agents"
                }
            };
        }
        
        protected override string[] GetRequiredParameters()
        {
            return new string[0];
        }
        
        public override string GetDisplaySummary(Dictionary<string, object> parameters)
        {
            var agentId = GetParameter<string>(parameters, "agent_id", "all");
            if (agentId == "all" || string.IsNullOrEmpty(agentId))
            {
                return "Checking status of all agents";
            }
            else
            {
                return $"Checking status of {agentId}";
            }
        }
        
        public override Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters)
        {
            try
            {
                var agentId = parameters.ContainsKey("agent_id") ? 
                    parameters["agent_id"].ToString() : "all";
                
                if (agentId == "all" || string.IsNullOrEmpty(agentId))
                {
                    var allStatuses = AgentManager.Instance.GetAllAgentStatuses();
                    
                    if (!allStatuses.Any())
                    {
                        return Task.FromResult(CreateSuccessResult(
                            new Dictionary<string, object> { ["agents"] = new List<object>() },
                            "No active agents"
                        ));
                    }
                    
                    var output = allStatuses.Select(s => new Dictionary<string, object>
                    {
                        ["agent_id"] = s.AgentId,
                        ["name"] = s.Name,
                        ["status"] = s.Status,
                        ["current_task"] = s.CurrentTask ?? "None",
                        ["task_id"] = s.TaskId ?? "",
                        ["is_idle"] = s.IsIdle,
                        ["running_time"] = s.RunningTime.TotalSeconds
                    }).ToList();
                    
                    var formatted = "Active Agents:\n";
                    foreach (var status in allStatuses)
                    {
                        formatted += $"- {status.Name} ({status.AgentId}): {status.Status}";
                        if (!string.IsNullOrEmpty(status.CurrentTask))
                        {
                            formatted += $" - Working on: {status.CurrentTask}";
                        }
                        formatted += "\n";
                    }
                    
                    return Task.FromResult(CreateSuccessResult(
                        new Dictionary<string, object> { ["agents"] = output },
                        formatted
                    ));
                }
                else
                {
                    var status = AgentManager.Instance.GetAgentStatus(agentId!);
                    
                    if (!status.Exists)
                    {
                        return Task.FromResult(CreateErrorResult($"Agent {agentId} not found"));
                    }
                    
                    return Task.FromResult(CreateSuccessResult(
                        new Dictionary<string, object>
                        {
                            ["agent_id"] = status.AgentId,
                            ["name"] = status.Name,
                            ["status"] = status.Status,
                            ["current_task"] = status.CurrentTask ?? "None",
                            ["task_id"] = status.TaskId ?? "",
                            ["is_idle"] = status.IsIdle,
                            ["running_time"] = status.RunningTime.TotalSeconds
                        },
                        $"{status.Name}: {status.Status}" + 
                            (status.CurrentTask != null ? $" - {status.CurrentTask}" : "")
                    ));
                }
            }
            catch (Exception ex)
            {
                return Task.FromResult(CreateErrorResult($"Failed to get agent status: {ex.Message}"));
            }
        }
    }
}