using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Saturn.Tools.Core;
using Saturn.Agents.MultiAgent;

namespace Saturn.Tools.MultiAgent
{
    public class CreateAgentTool : ToolBase
    {
        public override string Name => "create_agent";
        
        public override string Description => "Create a new sub-agent with specific capabilities and purpose";
        
        protected override Dictionary<string, object> GetParameterProperties()
        {
            return new Dictionary<string, object>
            {
                ["name"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["description"] = "Name for the new agent"
                },
                ["purpose"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["description"] = "The specific purpose and capabilities of this agent"
                },
                ["model"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["description"] = "Model to use (default: anthropic/claude-3.5-sonnet)"
                },
                ["enable_tools"] = new Dictionary<string, object>
                {
                    ["type"] = "boolean",
                    ["description"] = "Whether the agent should have access to tools"
                }
            };
        }
        
        protected override string[] GetRequiredParameters()
        {
            return new[] { "name", "purpose" };
        }
        
        public override string GetDisplaySummary(Dictionary<string, object> parameters)
        {
            var name = GetParameter<string>(parameters, "name", "");
            var purpose = GetParameter<string>(parameters, "purpose", "");
            var displayPurpose = TruncateString(purpose, 30);
            return $"Creating agent '{name}' for: {displayPurpose}";
        }
        
        public override async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters)
        {
            try
            {
                var name = parameters["name"].ToString()!;
                var purpose = parameters["purpose"].ToString()!;
                var model = parameters.ContainsKey("model") ? 
                    parameters["model"].ToString()! : "anthropic/claude-3.5-sonnet";
                var enableTools = parameters.ContainsKey("enable_tools") ? 
                    Convert.ToBoolean(parameters["enable_tools"]) : true;
                
                var result = await AgentManager.Instance.TryCreateSubAgent(name, purpose, model, enableTools);
                
                if (result.success)
                {
                    return CreateSuccessResult(
                        new Dictionary<string, object>
                        {
                            ["agent_id"] = result.result,
                            ["name"] = name,
                            ["purpose"] = purpose,
                            ["model"] = model
                        },
                        $"Created agent '{name}' with ID: {result.result}"
                    );
                }
                else
                {
                    var currentCount = AgentManager.Instance.GetCurrentAgentCount();
                    var maxCount = AgentManager.Instance.GetMaxConcurrentAgents();
                    
                    var errorMessage = $"Cannot create agent '{name}': {result.result}\n";
                    errorMessage += $"Current agents: {currentCount}/{maxCount}\n";
                    
                    if (result.runningTaskIds != null && result.runningTaskIds.Count > 0)
                    {
                        errorMessage += $"Running tasks that you can wait for:\n";
                        foreach (var taskId in result.runningTaskIds)
                        {
                            errorMessage += $"  - {taskId}\n";
                        }
                        errorMessage += "\nUse 'wait_for_agent' with these task IDs or terminate idle agents to free up capacity.";
                    }
                    else
                    {
                        errorMessage += "All agents are idle. Consider terminating unused agents to free up capacity.";
                    }
                    
                    return CreateErrorResult(errorMessage);
                }
            }
            catch (Exception ex)
            {
                return CreateErrorResult($"Failed to create agent: {ex.Message}");
            }
        }
    }
}