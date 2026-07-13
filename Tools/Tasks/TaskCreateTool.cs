using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Saturn.Data.Tasks;
using Saturn.Tools.Core;

namespace Saturn.Tools.Tasks
{
    public class TaskCreateTool : TaskToolBase
    {
        public override string Name => "create_task";

        public override string Description => @"Create a task on one of the user's todo lists (global, project or agent scope).

Defaults: sub-agents create on their own agent board; the orchestrator creates on the project board.
Supports blocking dependencies (blocked_by), recurrence (interval seconds or a cron expression), and flags.
Sub-agents may add tasks to project or global lists but cannot mark tasks agent-available.";

        protected override Dictionary<string, object> GetParameterProperties()
        {
            return new Dictionary<string, object>
            {
                ["title"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Short task title" },
                ["notes"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Details, context, acceptance criteria" },
                ["scope"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["enum"] = new[] { "global", "project", "agent" },
                    ["description"] = "Where the task lives. Defaults to your own agent board (sub-agents) or project (orchestrator)."
                },
                ["board"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Board name within the scope (default 'default'; for agent scope defaults to your agent name)" },
                ["priority"] = new Dictionary<string, object> { ["type"] = "string", ["enum"] = new[] { "low", "normal", "high" }, ["default"] = "normal" },
                ["blocked_by"] = new Dictionary<string, object>
                {
                    ["type"] = "array",
                    ["items"] = new Dictionary<string, object> { ["type"] = "string" },
                    ["description"] = "Task ids that must complete before this task is workable"
                },
                ["recurrence_interval_seconds"] = new Dictionary<string, object> { ["type"] = "integer", ["description"] = "Repeat every N seconds (min 60). Mutually exclusive with recurrence_cron." },
                ["recurrence_cron"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Cron expression for recurrence, evaluated in local time (e.g. '0 9 * * 1-5')" },
                ["agent_available"] = new Dictionary<string, object> { ["type"] = "boolean", ["default"] = false, ["description"] = "Orchestrator may auto-dispatch this task to sub-agents (orchestrator only)" },
                ["requires_approval"] = new Dictionary<string, object> { ["type"] = "boolean", ["default"] = false, ["description"] = "User must approve before the orchestrator claims this task" },
                ["user_handoff_only"] = new Dictionary<string, object> { ["type"] = "boolean", ["default"] = false, ["description"] = "Only the user may hand this task off; never auto-taken" }
            };
        }

        protected override string[] GetRequiredParameters() => new[] { "title" };

        public override string GetDisplaySummary(Dictionary<string, object> parameters)
        {
            return $"Creating task: {TruncateString(GetParameter<string>(parameters, "title", ""), 50)}";
        }

        public override async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters)
        {
            if (Store == null)
            {
                return CreateErrorResult(UnavailableError);
            }

            var caller = AgentContext.Current;
            var isSubAgent = caller?.ManagerAgentId != null;

            var scope = GetParameter<string?>(parameters, "scope", null)
                ?? (isSubAgent ? TaskScopes.Agent : TaskScopes.Project);
            var board = GetParameter<string?>(parameters, "board", null)
                ?? (scope == TaskScopes.Agent ? (caller?.AgentName ?? "default") : "default");

            var agentAvailable = GetParameter<bool>(parameters, "agent_available", false);
            if (agentAvailable && isSubAgent)
            {
                agentAvailable = false;
            }

            var intervalSeconds = parameters.ContainsKey("recurrence_interval_seconds")
                ? GetParameter<int?>(parameters, "recurrence_interval_seconds", null)
                : null;
            var cron = GetParameter<string?>(parameters, "recurrence_cron", null);
            var recurrenceKind = !string.IsNullOrWhiteSpace(cron) ? RecurrenceKinds.Cron
                : intervalSeconds.HasValue ? RecurrenceKinds.Interval
                : RecurrenceKinds.None;

            try
            {
                var task = await Store.CreateAsync(new TaskCreateSpec
                {
                    Title = GetParameter<string>(parameters, "title", ""),
                    Notes = GetParameter<string?>(parameters, "notes", null),
                    Scope = scope,
                    Board = board,
                    Priority = GetParameter<string>(parameters, "priority", "normal"),
                    CreatedBy = CallerName(),
                    BlockedBy = GetStringList(parameters, "blocked_by"),
                    RecurrenceKind = recurrenceKind,
                    RecurrenceIntervalSeconds = intervalSeconds,
                    RecurrenceCron = cron,
                    AgentAvailable = agentAvailable,
                    RequiresApproval = GetParameter<bool>(parameters, "requires_approval", false),
                    UserHandoffOnly = GetParameter<bool>(parameters, "user_handoff_only", false)
                });

                var view = await Store.BuildViewAsync(task);
                return CreateSuccessResult(new { taskId = task.Id }, $"Task created:\n{FormatTask(view)}");
            }
            catch (ArgumentException ex)
            {
                return CreateErrorResult(ex.Message);
            }
        }
    }
}
