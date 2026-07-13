using System.Collections.Generic;
using System.Threading.Tasks;
using Saturn.Tools.Core;

namespace Saturn.Tools.Tasks
{
    public class WaitForTaskTool : TaskToolBase
    {
        public override string Name => "wait_for_task";

        public override string Description => @"Register to be automatically re-prompted when another task completes, instead of polling.

Accepts a Saturn task id (tk_...) or an agent-manager task id (task_...) from hand_off_to_agent.
After registering, END YOUR TURN with a brief status report. When the target completes you will be
re-prompted automatically with its result — even days or weeks later, and even across restarts.
If the target already completed, the result is returned immediately instead.";

        protected override Dictionary<string, object> GetParameterProperties()
        {
            return new Dictionary<string, object>
            {
                ["task_id"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["description"] = "The task to wait on: tk_... (Saturn task) or task_... (agent handoff task)"
                },
                ["prompt"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["description"] = "Optional custom continuation prompt delivered when the target completes"
                }
            };
        }

        protected override string[] GetRequiredParameters() => new[] { "task_id" };

        public override string GetDisplaySummary(Dictionary<string, object> parameters)
        {
            return $"Waiting on {GetParameter<string>(parameters, "task_id", "")}";
        }

        public override async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters)
        {
            if (Store == null || Coordinator == null)
            {
                return CreateErrorResult(UnavailableError);
            }

            var targetId = GetParameter<string>(parameters, "task_id", "");
            if (string.IsNullOrWhiteSpace(targetId))
            {
                return CreateErrorResult("task_id is required");
            }

            var (registered, message) = await Coordinator.RegisterWaiterAsync(
                targetId.Trim(),
                GetParameter<string?>(parameters, "prompt", null));

            return CreateSuccessResult(new { registered }, message);
        }
    }
}
