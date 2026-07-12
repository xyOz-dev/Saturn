using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Saturn.Data.Tasks;
using Saturn.Tools.Core;

namespace Saturn.Tools.Tasks
{
    public class TaskListTool : TaskToolBase
    {
        public override string Name => "list_tasks";

        public override string Description => @"List tasks from the Saturn task system (the user's todo lists).

Scopes: 'global' (machine-wide list), 'project' (this repository, organized in named boards), 'agent' (per-agent todo lists, board = agent name).
Each task shows its id, status, blocking dependencies, flags (agent-available / requires-approval / user-handoff-only), recurrence and dispatch state.
Use this to see what work exists before claiming, dispatching or adding tasks.";

        protected override Dictionary<string, object> GetParameterProperties()
        {
            return new Dictionary<string, object>
            {
                ["scope"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["enum"] = new[] { "global", "project", "agent" },
                    ["description"] = "Filter by scope. Omit for all scopes."
                },
                ["board"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["description"] = "Filter by board name (for agent scope this is the agent name)."
                },
                ["include_done"] = new Dictionary<string, object>
                {
                    ["type"] = "boolean",
                    ["default"] = false,
                    ["description"] = "Include completed/failed/cancelled tasks."
                }
            };
        }

        protected override string[] GetRequiredParameters() => System.Array.Empty<string>();

        public override string GetDisplaySummary(Dictionary<string, object> parameters)
        {
            var scope = GetParameter<string>(parameters, "scope", "all");
            return $"Listing tasks ({scope})";
        }

        public override async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters)
        {
            if (Store == null)
            {
                return CreateErrorResult(UnavailableError);
            }

            var scope = GetParameter<string?>(parameters, "scope", null);
            var board = GetParameter<string?>(parameters, "board", null);
            var includeDone = GetParameter<bool>(parameters, "include_done", false);

            var views = await Store.ListAsync(scope, board, null, includeDone);
            if (views.Count == 0)
            {
                return CreateSuccessResult(new { count = 0 }, "No tasks found.");
            }

            var sb = new StringBuilder($"{views.Count} task(s):\n");
            foreach (var group in views.GroupBy(v => $"{v.Task.Scope}/{v.Task.Board}"))
            {
                sb.AppendLine($"\n== {group.Key} ==");
                foreach (var view in group.OrderBy(v => v.Task.SortOrder))
                {
                    sb.AppendLine(FormatTask(view));
                }
            }
            return CreateSuccessResult(new { count = views.Count }, sb.ToString());
        }
    }
}
