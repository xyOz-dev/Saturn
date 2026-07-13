using System.Collections.Generic;
using System.Threading.Tasks;
using Saturn.Tools.Core;

namespace Saturn.Tools.Tasks
{
    public class TaskClaimTool : TaskToolBase
    {
        public override string Name => "claim_task";

        public override string Description => @"(Orchestrator only) Claim a task from the user's todo lists so you can work on it or dispatch it.

Tasks flagged requires-approval enter the user's approval queue; you will be notified of the decision
by a scheduler message — do not start work until approved. Tasks flagged user-handoff-only cannot be claimed.";

        protected override Dictionary<string, object> GetParameterProperties()
        {
            return new Dictionary<string, object>
            {
                ["id"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Task id (tk_...)" }
            };
        }

        protected override string[] GetRequiredParameters() => new[] { "id" };

        public override string GetDisplaySummary(Dictionary<string, object> parameters)
        {
            return $"Claiming task {GetParameter<string>(parameters, "id", "")}";
        }

        public override async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters)
        {
            if (Store == null || Coordinator == null)
            {
                return CreateErrorResult(UnavailableError);
            }
            if (!IsOrchestratorCaller)
            {
                return CreateErrorResult("claim_task is only available to the orchestrator agent.");
            }

            var (status, message) = await Coordinator.ClaimTaskAsync(GetParameter<string>(parameters, "id", ""));
            return status == "error" ? CreateErrorResult(message) : CreateSuccessResult(new { status }, message);
        }
    }
}
