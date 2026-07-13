using System.Collections.Generic;
using System.Linq;
using System.Text;
using Saturn.Core.Tasks;
using Saturn.Data.Tasks;
using Saturn.Tools.Core;

namespace Saturn.Tools.Tasks
{
    public abstract class TaskToolBase : ToolBase
    {
        protected static TaskStore? Store => TaskSystem.Store;
        protected static TaskCoordinator? Coordinator => TaskSystem.Coordinator;

        protected const string UnavailableError = "The task system is only available when Saturn runs in web mode (saturn --web).";

        protected static bool IsOrchestratorCaller => AgentContext.Current?.IsOrchestrator == true;

        protected static string CallerName()
        {
            var ctx = AgentContext.Current;
            if (ctx == null) return "user";
            if (ctx.IsOrchestrator) return "orchestrator";
            return string.IsNullOrEmpty(ctx.AgentName) ? "agent" : ctx.AgentName;
        }

        protected static List<string> GetStringList(Dictionary<string, object> parameters, string key)
        {
            if (!parameters.TryGetValue(key, out var value))
            {
                return new List<string>();
            }
            return value switch
            {
                List<object> list => list.Select(x => x?.ToString() ?? "").Where(s => s.Length > 0).ToList(),
                object[] array => array.Select(x => x?.ToString() ?? "").Where(s => s.Length > 0).ToList(),
                string single => new List<string> { single },
                _ => new List<string>()
            };
        }

        protected static string FormatTask(TaskView view)
        {
            var t = view.Task;
            var sb = new StringBuilder();
            sb.Append($"[{t.Id}] {t.Title} — {t.Status}");
            if (view.Blocked)
            {
                var blockers = view.BlockedBy.Where(b => !b.Satisfied).Select(b => b.Id);
                sb.Append($" (BLOCKED by {string.Join(", ", blockers)})");
            }
            sb.Append($" | scope={t.Scope}/{t.Board} priority={t.Priority}");
            if (t.AgentAvailable) sb.Append(" agent-available");
            if (t.RequiresApproval) sb.Append(" requires-approval");
            if (t.UserHandoffOnly) sb.Append(" user-handoff-only");
            if (t.ClaimStatus != ClaimStatuses.None) sb.Append($" claim={t.ClaimStatus}");
            if (view.RecurrenceDescription != null) sb.Append($" recurs[{view.RecurrenceDescription}]");
            if (view.DispatchedTo != null) sb.Append($" dispatched-to={view.DispatchedTo}");
            if (!string.IsNullOrEmpty(t.Notes)) sb.Append($"\n    notes: {t.Notes}");
            return sb.ToString();
        }
    }
}
