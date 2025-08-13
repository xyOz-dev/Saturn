using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Saturn.Tools.Core;
using Saturn.Agents.MultiAgent;

namespace Saturn.Tools.MultiAgent
{
    public class TerminateAgentTool : ToolBase
    {
        public override string Name => "terminate_agent";
        
        public override string Description => "Terminate a sub-agent to free up capacity. Use this when an agent is no longer needed or to make room for new agents.";
        
        protected override Dictionary<string, object> GetParameterProperties()
        {
            return new Dictionary<string, object>
            {
                ["agent_id"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["description"] = "The ID of the agent to terminate"
                }
            };
        }
        
        protected override string[] GetRequiredParameters()
        {
            return new[] { "agent_id" };
        }
        
        public override string GetDisplaySummary(Dictionary<string, object> parameters)
        {
            var agentId = GetParameter<string>(parameters, "agent_id", "");
            return $"Terminating agent: {agentId}";
        }
        
        public override async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters)
        {
            try
            {
                var agentId = parameters["agent_id"].ToString()!;
                
                var agents = AgentManager.Instance.GetAllAgentStatuses();
                var agentExists = false;
                string? agentName = null;
                
                foreach (var agent in agents)
                {
                    if (agent.AgentId == agentId)
                    {
                        agentExists = true;
                        agentName = agent.Name;
                        break;
                    }
                }
                
                if (!agentExists)
                {
                    return CreateErrorResult($"Agent with ID '{agentId}' not found. Use 'get_agent_status' to see available agents.");
                }
                
                AgentManager.Instance.TerminateAgent(agentId);
                
                var currentCount = AgentManager.Instance.GetCurrentAgentCount();
                var maxCount = AgentManager.Instance.GetMaxConcurrentAgents();
                
                return CreateSuccessResult(
                    new Dictionary<string, object>
                    {
                        ["agent_id"] = agentId,
                        ["agent_name"] = agentName ?? "Unknown",
                        ["current_agent_count"] = currentCount,
                        ["max_agent_count"] = maxCount,
                        ["freed_capacity"] = 1
                    },
                    $"Successfully terminated agent '{agentName}' ({agentId}). Current agents: {currentCount}/{maxCount}"
                );
            }
            catch (Exception ex)
            {
                return CreateErrorResult($"Failed to terminate agent: {ex.Message}");
            }
        }
    }
}