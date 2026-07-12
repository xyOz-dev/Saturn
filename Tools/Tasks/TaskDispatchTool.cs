using System.Collections.Generic;
using System.Threading.Tasks;
using Saturn.Agents.MultiAgent;
using Saturn.Config;
using Saturn.Tools.Core;

namespace Saturn.Tools.Tasks
{
    public class TaskDispatchTool : TaskToolBase
    {
        public override string Name => "dispatch_task";

        public override string Description => @"(Orchestrator only) Dispatch a Saturn task to a sub-agent. The agent receives the task brief,
its final report is stored as the task result, the task auto-completes, and you are notified.

Pass agent_id to use an existing agent, or agent_name + agent_purpose to create a new one.
Refuses blocked tasks, user-handoff-only tasks, tasks pending claim approval, and already-dispatched tasks.";

        protected override Dictionary<string, object> GetParameterProperties()
        {
            return new Dictionary<string, object>
            {
                ["id"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Task id (tk_...)" },
                ["agent_id"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Existing idle agent to dispatch to" },
                ["agent_name"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Name for a new agent (used when agent_id is omitted)" },
                ["agent_purpose"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Purpose for a new agent (used when agent_id is omitted)" }
            };
        }

        protected override string[] GetRequiredParameters() => new[] { "id" };

        public override string GetDisplaySummary(Dictionary<string, object> parameters)
        {
            return $"Dispatching task {GetParameter<string>(parameters, "id", "")}";
        }

        public override async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters)
        {
            if (Store == null || Coordinator == null)
            {
                return CreateErrorResult(UnavailableError);
            }
            if (!IsOrchestratorCaller)
            {
                return CreateErrorResult("dispatch_task is only available to the orchestrator agent.");
            }

            var taskId = GetParameter<string>(parameters, "id", "");
            var task = await Store.FindAsync(taskId);
            if (task == null)
            {
                return CreateErrorResult($"Task {taskId} not found");
            }
            if (task.UserHandoffOnly)
            {
                return CreateErrorResult($"Task {taskId} is user-handoff-only; the user must dispatch it from the web UI.");
            }
            if (task.ClaimStatus == Saturn.Data.Tasks.ClaimStatuses.PendingApproval)
            {
                return CreateErrorResult($"Task {taskId} is awaiting the user's claim approval; wait for the decision.");
            }
            if (task.RequiresApproval && task.ClaimStatus != Saturn.Data.Tasks.ClaimStatuses.Approved)
            {
                return CreateErrorResult($"Task {taskId} requires approval — call claim_task first and wait for the user's decision.");
            }

            var agentId = GetParameter<string?>(parameters, "agent_id", null);
            string agentName;

            if (string.IsNullOrWhiteSpace(agentId))
            {
                var newName = GetParameter<string?>(parameters, "agent_name", null);
                var purpose = GetParameter<string?>(parameters, "agent_purpose", null);
                if (string.IsNullOrWhiteSpace(newName) || string.IsNullOrWhiteSpace(purpose))
                {
                    return CreateErrorResult("Provide agent_id, or agent_name and agent_purpose to create a new agent.");
                }

                var prefs = SubAgentPreferences.Instance;
                var (created, result, _) = await AgentManager.Instance.TryCreateSubAgent(
                    newName, purpose, prefs.DefaultModel, prefs.DefaultEnableTools,
                    prefs.DefaultTemperature, prefs.DefaultMaxTokens, prefs.DefaultTopP);
                if (!created)
                {
                    return CreateErrorResult(result);
                }
                agentId = result;
                agentName = newName;
            }
            else
            {
                var status = AgentManager.Instance.GetAgentStatus(agentId);
                if (!status.Exists)
                {
                    return CreateErrorResult($"Agent {agentId} not found");
                }
                if (!status.IsIdle)
                {
                    return CreateErrorResult($"Agent {agentId} is busy with task {status.TaskId}; wait or use another agent.");
                }
                agentName = status.Name;
            }

            var (ok, message, _) = await Coordinator.DispatchTaskAsync(taskId, agentId!, agentName);
            return ok
                ? CreateSuccessResult(new { taskId, agentId }, message + "\nYou will be notified when it completes; you can also wait_for_task on it.")
                : CreateErrorResult(message);
        }
    }
}
