using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Saturn.Tools.Core;
using Saturn.Agents.MultiAgent;
using Saturn.Config;

namespace Saturn.Tools.MultiAgent
{
    public class SpawnAgentTool : ToolBase
    {
        public override string Name => "spawn_agent";

        public override string Description => @"Spawn a sub-agent to complete a task and return its report. This is your primary tool for delegation.

When to use:
- Independent work that can run in parallel with yours
- Tasks that mean reading across many files when you only need the conclusion, not the file contents
- Large self-contained subtasks (refactoring one module, writing a test suite, researching a library)

When NOT to use:
- Small changes you can make directly yourself
- Single-fact lookups where you already know the file to check

How to use:
- Pick the narrowest agent_type that fits the task; types with fewer tools stay focused and cannot cause side effects.
- The agent starts fresh with no memory of this conversation. Include everything it needs in 'task': the goal, relevant file paths, constraints, and what its report should contain.
- By default this call blocks until the agent finishes and returns its report as the tool result. The report is returned to you, not shown to the user, so relay what matters.
- Set background=true to get a task_id back immediately instead; collect the result later with wait_for_agent. Spawn several background agents in one message to run them in parallel.
- The agent is single-use: it is cleaned up automatically after its task completes.

Rules:
- Once you delegate a task, do not do it yourself as well - wait for the result and integrate it.
- Work completed by a sub-agent is done. Review its report and integrate; never redo it.";

        protected override Dictionary<string, object> GetParameterProperties()
        {
            return new Dictionary<string, object>
            {
                ["name"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["description"] = "Short display name for the agent, e.g. 'code-explorer' or 'test-writer'"
                },
                ["task"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["description"] = "Self-contained instructions: the goal, relevant file paths, constraints, and what the report should contain. The agent cannot see this conversation."
                },
                ["agent_type"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["enum"] = AgentTypeRegistry.Names,
                    ["default"] = AgentTypeRegistry.DefaultTypeName,
                    ["description"] = "The kind of agent to spawn:\n" + AgentTypeRegistry.DescribeAll()
                },
                ["purpose"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["description"] = "Optional one-line role statement for the agent's system prompt, e.g. 'Explore code and report findings without modifying anything'. Defaults to the agent_type's standard role."
                },
                ["background"] = new Dictionary<string, object>
                {
                    ["type"] = "boolean",
                    ["default"] = false,
                    ["description"] = "If true, return a task_id immediately instead of waiting; collect the result with wait_for_agent"
                },
                ["timeout_seconds"] = new Dictionary<string, object>
                {
                    ["type"] = "number",
                    ["default"] = 600,
                    ["description"] = "How long to wait for the agent before returning control (default: 600). Only used when background is false; on timeout the agent keeps running and you get a task_id to wait on."
                }
            };
        }

        protected override string[] GetRequiredParameters()
        {
            return new[] { "name", "task" };
        }

        public override string GetDisplaySummary(Dictionary<string, object> parameters)
        {
            var name = GetParameter<string>(parameters, "name", "agent");
            var task = GetParameter<string>(parameters, "task", "");
            var background = GetParameter<bool>(parameters, "background", false);
            var suffix = background ? " (background)" : "";
            return $"Spawning {name}{suffix}: {TruncateString(task, 40)}";
        }

        public override async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters)
        {
            try
            {
                var name = parameters["name"].ToString()!;
                var task = parameters["task"].ToString()!;

                var agentTypeName = GetParameter<string?>(parameters, "agent_type", null);
                AgentTypeDefinition agentType;
                if (string.IsNullOrWhiteSpace(agentTypeName))
                {
                    agentType = AgentTypeRegistry.Default;
                }
                else if (!AgentTypeRegistry.TryGet(agentTypeName, out agentType))
                {
                    return CreateErrorResult(
                        $"Unknown agent_type '{agentTypeName}'. Valid types: {string.Join(", ", AgentTypeRegistry.Names)}");
                }

                var purpose = GetParameter<string?>(parameters, "purpose", null);
                if (string.IsNullOrWhiteSpace(purpose))
                {
                    purpose = agentType.Description;
                }
                var background = GetParameter<bool>(parameters, "background", false);

                var prefs = SubAgentPreferences.Instance;
                var config = prefs.GetConfigurationForPurpose(agentType.Name);
                var timeoutSeconds = parameters.ContainsKey("timeout_seconds")
                    ? Convert.ToInt32(parameters["timeout_seconds"])
                    : prefs.SpawnAgentTimeoutSeconds;

                var created = await AgentManager.Instance.TryCreateSubAgent(
                    name,
                    purpose,
                    config.Model,
                    config.EnableTools,
                    config.Temperature,
                    config.MaxTokens,
                    config.TopP,
                    config.SystemPromptOverride,
                    disposeOnTaskCompletion: true,
                    allowedTools: agentType.ToolNames,
                    systemPromptAddendum: agentType.SystemPromptAddendum
                );

                if (!created.success)
                {
                    var errorMessage = $"Cannot spawn agent '{name}': {created.result}\n";
                    errorMessage += $"Current agents: {AgentManager.Instance.GetCurrentAgentCount()}/{AgentManager.Instance.GetMaxConcurrentAgents()}\n";
                    if (created.runningTaskIds != null && created.runningTaskIds.Count > 0)
                    {
                        errorMessage += "Running tasks you can wait for with wait_for_agent:\n";
                        errorMessage += string.Join("\n", created.runningTaskIds.Select(id => $"  - {id}"));
                    }
                    return CreateErrorResult(errorMessage);
                }

                var agentId = created.result;
                string taskId;
                try
                {
                    taskId = await AgentManager.Instance.HandOffTask(agentId, task);
                }
                catch (Exception)
                {
                    AgentManager.Instance.TerminateAgent(agentId);
                    throw;
                }

                if (background)
                {
                    return CreateSuccessResult(
                        new Dictionary<string, object>
                        {
                            ["task_id"] = taskId,
                            ["agent_id"] = agentId,
                            ["agent_name"] = name,
                            ["status"] = "running"
                        },
                        $"Spawned {name} in the background. Task ID: {taskId}. Collect the result with wait_for_agent when you need it."
                    );
                }

                var results = await AgentManager.Instance.WaitForAllTasks(
                    new List<string> { taskId }, timeoutSeconds * 1000);
                var result = results.FirstOrDefault();

                if (result == null)
                {
                    return CreateSuccessResult(
                        new Dictionary<string, object>
                        {
                            ["task_id"] = taskId,
                            ["agent_id"] = agentId,
                            ["agent_name"] = name,
                            ["status"] = "running"
                        },
                        $"{name} is still working after {timeoutSeconds}s. It keeps running; retrieve its report with wait_for_agent on task ID {taskId}."
                    );
                }

                if (!result.Success)
                {
                    return CreateErrorResult($"Sub-agent {name} failed after {result.Duration.TotalSeconds:F1}s: {result.Result}");
                }

                return CreateSuccessResult(
                    new Dictionary<string, object>
                    {
                        ["task_id"] = taskId,
                        ["agent_id"] = agentId,
                        ["agent_name"] = name,
                        ["status"] = "completed",
                        ["duration_seconds"] = result.Duration.TotalSeconds,
                        ["result"] = result.Result
                    },
                    $"{name} completed in {result.Duration.TotalSeconds:F1}s.\n\nReport:\n{result.Result}"
                );
            }
            catch (Exception ex)
            {
                return CreateErrorResult($"Failed to spawn agent: {ex.Message}");
            }
        }
    }
}
