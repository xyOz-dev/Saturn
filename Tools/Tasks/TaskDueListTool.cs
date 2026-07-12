using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Saturn.Data.Tasks;
using Saturn.Tools.Core;

namespace Saturn.Tools.Tasks
{
    public class TaskDueListTool : TaskToolBase
    {
        public override string Name => "list_due_tasks";

        public override string Description => @"(Orchestrator only) Summarize actionable work: due recurring tasks, ready agent-available tasks,
tasks pending claim approval, and undelivered scheduler wakes. Use this to plan what to do next.";

        protected override Dictionary<string, object> GetParameterProperties() => new();

        protected override string[] GetRequiredParameters() => Array.Empty<string>();

        public override string GetDisplaySummary(Dictionary<string, object> parameters) => "Listing due and ready tasks";

        public override async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters)
        {
            if (Store == null)
            {
                return CreateErrorResult(UnavailableError);
            }
            if (!IsOrchestratorCaller)
            {
                return CreateErrorResult("list_due_tasks is only available to the orchestrator agent.");
            }

            var sb = new StringBuilder();
            var views = await Store.ListAsync(includeDone: false);

            var ready = views.Where(v => v.Task.AgentAvailable && !v.Task.UserHandoffOnly && !v.Blocked
                && v.Task.Status == TaskStatuses.Pending && v.DispatchedTo == null
                && v.Task.ClaimStatus is ClaimStatuses.None or ClaimStatuses.Approved).ToList();
            var pendingClaims = views.Where(v => v.Task.ClaimStatus == ClaimStatuses.PendingApproval).ToList();
            var dueSoon = views.Where(v => v.Task.NextRunAt.HasValue && v.Task.NextRunAt.Value <= DateTime.UtcNow.AddHours(1)).ToList();
            var dispatched = views.Where(v => v.DispatchedTo != null).ToList();
            var wakes = await Store.Project.GetPendingWakesAsync();

            sb.AppendLine($"Ready agent-available tasks: {ready.Count}");
            foreach (var v in ready) sb.AppendLine("  " + FormatTask(v));
            sb.AppendLine($"Recurring due within 1h: {dueSoon.Count}");
            foreach (var v in dueSoon) sb.AppendLine($"  [{v.Task.Id}] {v.Task.Title} next at {v.Task.NextRunAt:u}");
            sb.AppendLine($"Awaiting user claim approval: {pendingClaims.Count}");
            foreach (var v in pendingClaims) sb.AppendLine($"  [{v.Task.Id}] {v.Task.Title}");
            sb.AppendLine($"Currently dispatched: {dispatched.Count}");
            foreach (var v in dispatched) sb.AppendLine($"  [{v.Task.Id}] {v.Task.Title} → {v.DispatchedTo}");
            sb.AppendLine($"Undelivered scheduler wakes: {wakes.Count}");
            foreach (var w in wakes) sb.AppendLine($"  [{w.Kind}] {TruncateString(w.Prompt, 80)}");

            return CreateSuccessResult(new
            {
                ready = ready.Count,
                dueSoon = dueSoon.Count,
                pendingClaims = pendingClaims.Count,
                dispatched = dispatched.Count,
                pendingWakes = wakes.Count
            }, sb.ToString());
        }
    }
}
