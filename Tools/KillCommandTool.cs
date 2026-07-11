using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Saturn.Tools.Core;

namespace Saturn.Tools
{
    public class KillCommandTool : ToolBase
    {
        public override string Name => "kill_command";

        public override string Description => @"Stop a command started with execute_command (run_in_background: true).

Terminates the process and its child process tree. Use this to shut down dev servers, watchers, or any background command you no longer need.";

        protected override Dictionary<string, object> GetParameterProperties()
        {
            return new Dictionary<string, object>
            {
                { "command_id", new Dictionary<string, object>
                    {
                        { "type", "string" },
                        { "description", "The command_id returned by execute_command when it was started in the background" }
                    }
                }
            };
        }

        protected override string[] GetRequiredParameters()
        {
            return new[] { "command_id" };
        }

        public override string GetDisplaySummary(Dictionary<string, object> parameters)
        {
            var id = GetParameter<string>(parameters, "command_id", "");
            return $"Killing {id}";
        }

        public override Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters)
        {
            var commandId = GetParameter<string>(parameters, "command_id");
            if (string.IsNullOrWhiteSpace(commandId))
            {
                return Task.FromResult(CreateErrorResult("command_id parameter is required"));
            }

            var bg = BackgroundCommandManager.Instance.Get(commandId);
            if (bg == null)
            {
                return Task.FromResult(CreateErrorResult($"No background command with id '{commandId}'"));
            }

            if (bg.Status != BackgroundCommandStatus.Running)
            {
                return Task.FromResult(CreateSuccessResult(
                    new Dictionary<string, object> { ["command_id"] = commandId, ["status"] = bg.Status.ToString().ToLowerInvariant() },
                    $"Command '{commandId}' is already {bg.Status.ToString().ToLowerInvariant()}."));
            }

            try
            {
                bg.Process.Kill(entireProcessTree: true);
                bg.Status = BackgroundCommandStatus.Killed;
            }
            catch (Exception ex)
            {
                return Task.FromResult(CreateErrorResult($"Failed to kill command '{commandId}': {ex.Message}"));
            }

            return Task.FromResult(CreateSuccessResult(
                new Dictionary<string, object> { ["command_id"] = commandId, ["status"] = "killed" },
                $"Command '{commandId}' was killed."));
        }
    }
}
