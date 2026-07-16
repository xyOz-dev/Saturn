using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Saturn.Tools.Core;

namespace Saturn.Tools
{
    public class GetCommandOutputTool : ToolBase
    {
        public override string Name => "get_command_output";

        public override string Description => @"Read output produced by a command started with execute_command (run_in_background: true).

Returns only the output generated since the last call for that command_id, along with the command's current status (running, exited, or killed) and its exit code once finished. Call it repeatedly to follow a long-running process.

Notes:
- Reads are destructive: output returned once is not returned again, and lines excluded by 'filter' are consumed and cannot be retrieved later. Omit 'filter' if you may need the full log.
- Shortly after the status changes to exited, one more call may return final output that was still being flushed.";

        protected override Dictionary<string, object> GetParameterProperties()
        {
            return new Dictionary<string, object>
            {
                { "command_id", new Dictionary<string, object>
                    {
                        { "type", "string" },
                        { "description", "The command_id returned by execute_command when it was started in the background" }
                    }
                },
                { "filter", new Dictionary<string, object>
                    {
                        { "type", "string" },
                        { "description", "Optional regular expression; only output lines matching it are returned" }
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
            return $"Reading output of {id}";
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

            Regex? filterRegex = null;
            var filter = GetParameter<string>(parameters, "filter", "");
            if (!string.IsNullOrEmpty(filter))
            {
                try
                {
                    filterRegex = new Regex(filter, RegexOptions.None, TimeSpan.FromSeconds(2));
                }
                catch (ArgumentException ex)
                {
                    return Task.FromResult(CreateErrorResult($"Invalid filter regular expression: {ex.Message}"));
                }
            }

            var (stdout, stderr) = bg.ReadNew();

            if (filterRegex != null)
            {
                stdout = FilterLines(stdout, filterRegex);
                stderr = FilterLines(stderr, filterRegex);
            }

            var status = bg.Status.ToString().ToLowerInvariant();

            var output = new StringBuilder();
            output.AppendLine($"Command: {bg.Command}");
            output.AppendLine($"Status: {status}");
            if (bg.Status != BackgroundCommandStatus.Running && bg.ExitCode.HasValue)
            {
                output.AppendLine($"Exit Code: {bg.ExitCode.Value}");
            }
            output.AppendLine();

            if (!string.IsNullOrWhiteSpace(stdout))
            {
                output.AppendLine("=== New Standard Output ===");
                output.AppendLine(stdout.TrimEnd());
            }

            if (!string.IsNullOrWhiteSpace(stderr))
            {
                output.AppendLine("=== New Standard Error ===");
                output.AppendLine(stderr.TrimEnd());
            }

            if (string.IsNullOrWhiteSpace(stdout) && string.IsNullOrWhiteSpace(stderr))
            {
                output.AppendLine("(no new output)");
            }

            return Task.FromResult(CreateSuccessResult(
                new Dictionary<string, object>
                {
                    ["command_id"] = commandId,
                    ["status"] = status,
                    ["exit_code"] = bg.ExitCode as object ?? "",
                    ["stdout"] = stdout,
                    ["stderr"] = stderr
                },
                output.ToString()));
        }

        private static string FilterLines(string text, Regex regex)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            var matched = text
                .Split('\n')
                .Where(line => regex.IsMatch(line.TrimEnd('\r')));

            return string.Join("\n", matched);
        }
    }
}
