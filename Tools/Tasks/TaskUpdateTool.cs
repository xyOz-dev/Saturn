using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Saturn.Data.Tasks;
using Saturn.Tools.Core;

namespace Saturn.Tools.Tasks
{
    public class TaskUpdateTool : TaskToolBase
    {
        public override string Name => "update_task";

        public override string Description => @"Update an existing task: title, notes, status, priority, scope/board, dependencies, recurrence or flags.
Only pass the fields you want to change. To finish a task prefer complete_task.";

        protected override Dictionary<string, object> GetParameterProperties()
        {
            return new Dictionary<string, object>
            {
                ["id"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Task id (tk_...)" },
                ["title"] = new Dictionary<string, object> { ["type"] = "string" },
                ["notes"] = new Dictionary<string, object> { ["type"] = "string" },
                ["status"] = new Dictionary<string, object> { ["type"] = "string", ["enum"] = new[] { "pending", "in_progress", "done", "failed", "cancelled" } },
                ["priority"] = new Dictionary<string, object> { ["type"] = "string", ["enum"] = new[] { "low", "normal", "high" } },
                ["scope"] = new Dictionary<string, object> { ["type"] = "string", ["enum"] = new[] { "global", "project", "agent" } },
                ["board"] = new Dictionary<string, object> { ["type"] = "string" },
                ["blocked_by"] = new Dictionary<string, object>
                {
                    ["type"] = "array",
                    ["items"] = new Dictionary<string, object> { ["type"] = "string" },
                    ["description"] = "Replaces the full dependency list"
                },
                ["recurrence_interval_seconds"] = new Dictionary<string, object> { ["type"] = "integer" },
                ["recurrence_cron"] = new Dictionary<string, object> { ["type"] = "string" },
                ["clear_recurrence"] = new Dictionary<string, object> { ["type"] = "boolean", ["description"] = "Remove any recurrence from the task" },
                ["agent_available"] = new Dictionary<string, object> { ["type"] = "boolean" },
                ["requires_approval"] = new Dictionary<string, object> { ["type"] = "boolean" },
                ["user_handoff_only"] = new Dictionary<string, object> { ["type"] = "boolean" }
            };
        }

        protected override string[] GetRequiredParameters() => new[] { "id" };

        public override string GetDisplaySummary(Dictionary<string, object> parameters)
        {
            return $"Updating task {GetParameter<string>(parameters, "id", "")}";
        }

        public override async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters)
        {
            if (Store == null)
            {
                return CreateErrorResult(UnavailableError);
            }

            var id = GetParameter<string>(parameters, "id", "");
            var isSubAgent = AgentContext.Current?.ManagerAgentId != null;

            string? recurrenceKind = null;
            int? intervalSeconds = null;
            string? cron = null;
            if (GetParameter<bool>(parameters, "clear_recurrence", false))
            {
                recurrenceKind = RecurrenceKinds.None;
            }
            else if (parameters.ContainsKey("recurrence_cron"))
            {
                recurrenceKind = RecurrenceKinds.Cron;
                cron = GetParameter<string?>(parameters, "recurrence_cron", null);
            }
            else if (parameters.ContainsKey("recurrence_interval_seconds"))
            {
                recurrenceKind = RecurrenceKinds.Interval;
                intervalSeconds = GetParameter<int?>(parameters, "recurrence_interval_seconds", null);
            }

            bool? agentAvailable = parameters.ContainsKey("agent_available")
                ? GetParameter<bool>(parameters, "agent_available", false)
                : null;
            if (agentAvailable == true && isSubAgent)
            {
                return CreateErrorResult("Sub-agents cannot mark tasks agent-available.");
            }

            try
            {
                var task = await Store.UpdateAsync(id, new TaskUpdateSpec
                {
                    Title = GetParameter<string?>(parameters, "title", null),
                    Notes = parameters.ContainsKey("notes") ? GetParameter<string?>(parameters, "notes", null) : null,
                    Status = GetParameter<string?>(parameters, "status", null),
                    Priority = GetParameter<string?>(parameters, "priority", null),
                    Scope = GetParameter<string?>(parameters, "scope", null),
                    Board = GetParameter<string?>(parameters, "board", null),
                    BlockedBy = parameters.ContainsKey("blocked_by") ? GetStringList(parameters, "blocked_by") : null,
                    RecurrenceKind = recurrenceKind,
                    RecurrenceIntervalSeconds = intervalSeconds,
                    RecurrenceCron = cron,
                    AgentAvailable = agentAvailable,
                    RequiresApproval = parameters.ContainsKey("requires_approval") ? GetParameter<bool>(parameters, "requires_approval", false) : null,
                    UserHandoffOnly = parameters.ContainsKey("user_handoff_only") ? GetParameter<bool>(parameters, "user_handoff_only", false) : null
                });

                if (task == null)
                {
                    return CreateErrorResult($"Task {id} not found");
                }
                var view = await Store.BuildViewAsync(task);
                return CreateSuccessResult(new { taskId = task.Id }, $"Task updated:\n{FormatTask(view)}");
            }
            catch (ArgumentException ex)
            {
                return CreateErrorResult(ex.Message);
            }
        }
    }
}
