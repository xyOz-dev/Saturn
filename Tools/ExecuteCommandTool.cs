using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Saturn.Tools.Core;
using Saturn.Tools.Objects;

namespace Saturn.Tools
{
    public class ExecuteCommandTool : ToolBase
    {
        private const int DefaultTimeoutSeconds = 30;
        private const int MaxOutputLength = 1048576; // 1MB
        private readonly List<CommandHistory> _commandHistory = new();
        private readonly CommandExecutorConfig _config;
        private readonly ICommandApprovalService _approvalService;

        public ExecuteCommandTool() : this(new CommandExecutorConfig(), null!) { }

        public ExecuteCommandTool(CommandExecutorConfig config) : this(config, null!) { }

        public ExecuteCommandTool(CommandExecutorConfig config, ICommandApprovalService approvalService)
        {
            _config = config ?? new CommandExecutorConfig();
            _approvalService = approvalService ?? new CommandApprovalService(true);
        }

        public override string Name => "execute_command";

        public override string Description => @"Execute a system command and return its exit code, stdout, and stderr.

How commands run:
- On Windows the command is run with 'cmd.exe /c'; on Linux/macOS with '/bin/sh -c'. Use the syntax of the current platform.
- Commands run non-interactively with no stdin. Do not run commands that wait for user input (e.g. editors, REPLs, prompts) - they will hang until the timeout.
- Default timeout is 30 seconds; pass 'timeout' (in seconds) for longer-running commands like builds or test suites.
- Output is captured up to 1 MB; anything beyond that is truncated.
- The user may be asked to approve each command before it runs; a denied command returns an error.

When to use:
- Running builds, tests, linters, or type checkers
- Git operations
- Package manager commands
- Inspecting system or environment state

Prefer the dedicated file tools (read_file, write_file, grep, glob, list_files) over shell equivalents like cat, echo, or find.";

        protected override Dictionary<string, object> GetParameterProperties()
        {
            return new Dictionary<string, object>
            {
                { "command", new Dictionary<string, object>
                    {
                        { "type", "string" },
                        { "description", "The system command to execute" }
                    }
                },
                { "workingDirectory", new Dictionary<string, object>
                    {
                        { "type", "string" },
                        { "description", "The working directory for command execution. Defaults to current directory" }
                    }
                },
                { "timeout", new Dictionary<string, object>
                    {
                        { "type", "integer" },
                        { "default", 30 },
                        { "description", "Command timeout in seconds. Default is 30 seconds; increase for builds and test runs" }
                    }
                },
                { "captureOutput", new Dictionary<string, object>
                    {
                        { "type", "boolean" },
                        { "default", true },
                        { "description", "Whether to capture command output. Default is true" }
                    }
                },
                { "runAsShell", new Dictionary<string, object>
                    {
                        { "type", "boolean" },
                        { "default", true },
                        { "description", "Whether to run the command through the platform shell (cmd.exe or /bin/sh). Set to false to launch an executable directly. Default is true" }
                    }
                }
            };
        }

        protected override string[] GetRequiredParameters()
        {
            return new[] { "command" };
        }
        
        public override string GetDisplaySummary(Dictionary<string, object> parameters)
        {
            var command = GetParameter<string>(parameters, "command", "");
            return $"$ {TruncateString(command, 50)}";
        }

        public override async Task<ToolResult> ExecuteAsync(Dictionary<string, object> arguments)
        {
            try
            {
                var command = GetParameter<string>(arguments, "command");
                if (string.IsNullOrWhiteSpace(command))
                {
                    return CreateErrorResult("Command parameter is required");
                }
                
                var workingDirectory = GetParameter<string>(arguments, "workingDirectory", Directory.GetCurrentDirectory());
                var timeoutSeconds = GetParameter<int>(arguments, "timeout", _config.DefaultTimeout);
                var captureOutput = GetParameter<bool>(arguments, "captureOutput", true);
                var runAsShell = GetParameter<bool>(arguments, "runAsShell", true);

                if (AgentContext.RequireCommandApproval)
                {
                    var approved = await _approvalService.RequestApprovalAsync(command, workingDirectory);
                    if (!approved)
                    {
                        return CreateErrorResult("Command execution denied by user");
                    }
                }

                if (_config.SecurityMode != SecurityMode.Unrestricted)
                {
                    var validationResult = ValidateCommand(command);
                    if (!validationResult.IsValid)
                    {
                        return CreateErrorResult($"Command blocked: {validationResult.Reason}");
                    }
                }

                if (!Directory.Exists(workingDirectory))
                {
                    return CreateErrorResult($"Working directory does not exist: {workingDirectory}");
                }

                var historyEntry = new CommandHistory
                {
                    Command = command,
                    WorkingDirectory = workingDirectory,
                    ExecutedAt = DateTime.UtcNow
                };

                try
                {
                    var result = await ExecuteCommandAsync(
                        command,
                        workingDirectory,
                        TimeSpan.FromSeconds(timeoutSeconds),
                        captureOutput,
                        runAsShell,
                        CancellationToken.None);

                    historyEntry.ExitCode = result.ExitCode;
                    historyEntry.Duration = result.Duration;
                    historyEntry.Success = result.ExitCode == 0;

                    if (_config.EnableHistory)
                    {
                        _commandHistory.Add(historyEntry);
                        if (_commandHistory.Count > _config.MaxHistorySize)
                        {
                            _commandHistory.RemoveAt(0);
                        }
                    }

                    return FormatResult(result);
                }
                catch (Exception ex)
                {
                    historyEntry.Success = false;
                    historyEntry.Error = ex.Message;
                    
                    if (_config.EnableHistory)
                    {
                        _commandHistory.Add(historyEntry);
                    }

                    throw;
                }
            }
            catch (Exception ex)
            {
                return CreateErrorResult($"Command execution failed: {ex.Message}");
            }
        }

        private async Task<CommandResult> ExecuteCommandAsync(
            string command,
            string workingDirectory,
            TimeSpan timeout,
            bool captureOutput,
            bool runAsShell,
            CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();
            var processInfo = CreateProcessStartInfo(command, workingDirectory, captureOutput, runAsShell);

            using var process = new Process { StartInfo = processInfo };
            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            if (captureOutput)
            {
                process.OutputDataReceived += (sender, e) =>
                {
                    if (e.Data != null && outputBuilder.Length < MaxOutputLength)
                    {
                        outputBuilder.AppendLine(e.Data);
                    }
                };

                process.ErrorDataReceived += (sender, e) =>
                {
                    if (e.Data != null && errorBuilder.Length < MaxOutputLength)
                    {
                        errorBuilder.AppendLine(e.Data);
                    }
                };
            }

            process.Start();

            if (captureOutput)
            {
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout);

            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch { }

                throw new TimeoutException($"Command execution timed out after {timeout.TotalSeconds} seconds");
            }

            stopwatch.Stop();

            return new CommandResult
            {
                ExitCode = process.ExitCode,
                StandardOutput = outputBuilder.ToString(),
                StandardError = errorBuilder.ToString(),
                Duration = stopwatch.Elapsed,
                Command = command,
                WorkingDirectory = workingDirectory
            };
        }

        private ProcessStartInfo CreateProcessStartInfo(string command, string workingDirectory, bool captureOutput, bool runAsShell)
        {
            var startInfo = new ProcessStartInfo
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = captureOutput,
                RedirectStandardError = captureOutput,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            if (runAsShell)
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    startInfo.FileName = "cmd.exe";
                    startInfo.Arguments = $"/c {command}";
                }
                else
                {
                    startInfo.FileName = "/bin/sh";
                    startInfo.Arguments = $"-c \"{command.Replace("\"", "\\\"")}\"";
                }
            }
            else
            {
                var parts = ParseCommand(command);
                startInfo.FileName = parts.FileName;
                startInfo.Arguments = parts.Arguments;
            }

            return startInfo;
        }

        private (string FileName, string Arguments) ParseCommand(string command)
        {
            command = command.Trim();
            
            if (command.StartsWith("\""))
            {
                var endQuote = command.IndexOf("\"", 1);
                if (endQuote > 0)
                {
                    var fileName = command.Substring(1, endQuote - 1);
                    var arguments = command.Substring(endQuote + 1).Trim();
                    return (fileName, arguments);
                }
            }

            var firstSpace = command.IndexOf(' ');
            if (firstSpace > 0)
            {
                return (command.Substring(0, firstSpace), command.Substring(firstSpace + 1));
            }

            return (command, string.Empty);
        }

        private CommandValidationResult ValidateCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
            {
                return new CommandValidationResult { IsValid = false, Reason = "Command cannot be empty" };
            }

            if (_config.CustomValidator != null)
            {
                return _config.CustomValidator(command);
            }

            return new CommandValidationResult { IsValid = true };
        }

        private ToolResult FormatResult(CommandResult result)
        {
            var output = new StringBuilder();
            
            output.AppendLine($"Command: {result.Command}");
            output.AppendLine($"Working Directory: {result.WorkingDirectory}");
            output.AppendLine($"Exit Code: {result.ExitCode}");
            output.AppendLine($"Duration: {result.Duration.TotalMilliseconds:F2}ms");
            output.AppendLine();

            if (!string.IsNullOrWhiteSpace(result.StandardOutput))
            {
                output.AppendLine("=== Standard Output ===");
                output.AppendLine(TruncateOutput(result.StandardOutput));
            }

            if (!string.IsNullOrWhiteSpace(result.StandardError))
            {
                output.AppendLine("=== Standard Error ===");
                output.AppendLine(TruncateOutput(result.StandardError));
            }

            return result.ExitCode == 0 
                ? CreateSuccessResult(result, output.ToString()) 
                : CreateErrorResult($"Command exited with code {result.ExitCode}\n\n{output}");
        }

        private string TruncateOutput(string output)
        {
            if (output.Length <= MaxOutputLength)
                return output;

            return output.Substring(0, MaxOutputLength) + "\n\n... (output truncated)";
        }

        public IReadOnlyList<CommandHistory> GetCommandHistory() => _commandHistory.AsReadOnly();

        public void ClearHistory() => _commandHistory.Clear();
    }
}