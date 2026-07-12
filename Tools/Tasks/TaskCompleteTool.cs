using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Saturn.Data.Tasks;
using Saturn.Tools.Core;

namespace Saturn.Tools.Tasks
{
    public class TaskCompleteTool : TaskToolBase
    {
        public override string Name => "complete_task";

        public override string Description => @"Mark a task as completed (or failed). Refuses if the task is still blocked by dependencies.
Completing a recurring task records the run and re-arms it for the next occurrence.
Reports any tasks that became unblocked as a result.";

        protected override Dictionary<string, object> GetParameterProperties()
        {
            return new Dictionary<string, object>
            {
                ["id"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Task id (tk_...)" },
                ["success"] = new Dictionary<string, object> { ["type"] = "boolean", ["default"] = true, ["description"] = "false marks the task failed" },
                ["note"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Short outcome note" }
            };
        }

        protected override string[] GetRequiredParameters() => new[] { "id" };

        public override string GetDisplaySummary(Dictionary<string, object> parameters)
        {
            return $"Completing task {GetParameter<string>(parameters, "id", "")}";
        }

        public override async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters)
        {
            if (Store == null)
            {
                return CreateErrorResult(UnavailableError);
            }

            var id = GetParameter<string>(parameters, "id", "");
            var success = GetParameter<bool>(parameters, "success", true);
            var note = GetParameter<string?>(parameters, "note", null);

            if (await Store.IsBlockedAsync(id))
            {
                var blockers = await Store.GetBlockersAsync(id);
                return CreateErrorResult(
                    $"Task {id} is still blocked by: {string.Join(", ", blockers.Where(b => !TaskStatuses.IsTerminal(b.Status)).Select(b => $"{b.Title} ({b.Id})"))}. " +
                    "Complete the blockers first or remove the dependency with update_task.");
            }

            var task = await Store.CompleteAsync(id, success, note);
            if (task == null)
            {
                return CreateErrorResult($"Task {id} not found");
            }

            var unblocked = await Store.GetNewlyUnblockedAsync(id);
            var message = task.IsRecurring
                ? $"Recurring task {id} run recorded ({(success ? "done" : "failed")}); it is re-armed for {task.NextRunAt:u}."
                : $"Task {id} marked {task.Status}.";
            if (unblocked.Count > 0)
            {
                message += $"\nNow unblocked: {string.Join(", ", unblocked.Select(t => $"{t.Title} ({t.Id})"))}";
            }
            return CreateSuccessResult(new { taskId = id, status = task.Status }, message);
        }
    }
}
