using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Saturn.Core.Workspace;
using Saturn.Tools.Core;
using Saturn.Tools.Objects;

namespace Saturn.Tools
{
    public class ExecuteCommandTool : ToolBase
    {
        private const int DefaultTimeoutSeconds = 120;
        private const int MaxOutputLength = 1048576;
        private const int MaxCaptureLength = 8388608;
        private readonly List<CommandHistory> _commandHistory = new();
        private readonly object _historyLock = new();
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
- Default timeout is 120 seconds; pass 'timeout' (in seconds) for longer-running commands like builds or test suites. On timeout the process is killed and any output captured so far is still returned.
- Output is captured up to 8 MB (a marker is appended if more was discarded). At most 1 MB is returned; the middle is elided if it is longer, keeping the start and end.
- The user may be asked to approve each command before it runs; a denied command returns an error.
- For long-running processes (dev servers, watchers), set 'run_in_background' to true. The tool returns a command_id immediately; poll its output with get_command_output and stop it with kill_command.

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
                        { "default", 120 },
                        { "description", "Command timeout in seconds. Default is 120 seconds; increase for builds and test runs. Ignored when run_in_background is true" }
                    }
                },
                { "run_in_background", new Dictionary<string, object>
                    {
                        { "type", "boolean" },
                        { "default", false },
                        { "description", "Run the command in the background and return a command_id immediately instead of waiting. Use for dev servers, watchers, and other long-lived processes; read output with get_command_output and stop it with kill_command" }
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
                
                var workingDirectory = GetParameter<string>(arguments, "workingDirectory", WorkspaceManager.CurrentWorkspace);
                try
                {
                    // Relative paths must resolve against the active workspace, not the
                    // process CWD, or they escape workspace isolation after a switch.
                    workingDirectory = Path.GetFullPath(workingDirectory, WorkspaceManager.CurrentWorkspace);
                }
                catch (Exception ex)
                {
                    return CreateErrorResult($"Invalid working directory: {ex.Message}");
                }
                var timeoutSeconds = GetParameter<int>(arguments, "timeout", _config.DefaultTimeout);
                if (timeoutSeconds <= 0)
                {
                    return CreateErrorResult($"Invalid timeout: {timeoutSeconds}. It must be a positive number of seconds.");
                }
                var captureOutput = GetParameter<bool>(arguments, "captureOutput", true);
                var runAsShell = GetParameter<bool>(arguments, "runAsShell", true);
                var runInBackground = GetParameter<bool>(arguments, "run_in_background", false);

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

                if (runInBackground)
                {
                    return StartBackgroundCommand(command, workingDirectory, runAsShell);
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
                        lock (_historyLock)
                        {
                            _commandHistory.Add(historyEntry);
                            if (_commandHistory.Count > _config.MaxHistorySize)
                            {
                                _commandHistory.RemoveAt(0);
                            }
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
                        lock (_historyLock)
                        {
                            _commandHistory.Add(historyEntry);
                            if (_commandHistory.Count > _config.MaxHistorySize)
                            {
                                _commandHistory.RemoveAt(0);
                            }
                        }
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

            var outputDropped = false;
            var errorDropped = false;

            if (captureOutput)
            {
                process.OutputDataReceived += (sender, e) =>
                {
                    if (e.Data == null) return;
                    if (outputBuilder.Length < MaxCaptureLength)
                    {
                        outputBuilder.AppendLine(e.Data);
                    }
                    else
                    {
                        outputDropped = true;
                    }
                };

                process.ErrorDataReceived += (sender, e) =>
                {
                    if (e.Data == null) return;
                    if (errorBuilder.Length < MaxCaptureLength)
                    {
                        errorBuilder.AppendLine(e.Data);
                    }
                    else
                    {
                        errorDropped = true;
                    }
                };
            }

            var exitTcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            process.EnableRaisingEvents = true;
            process.Exited += (sender, e) => exitTcs.TrySetResult(null);

            process.Start();

            if (captureOutput)
            {
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout);

            var timedOut = false;
            var pipeHeldOpen = false;
            try
            {
                // Wait on process exit rather than WaitForExitAsync alone: WaitForExitAsync
                // also waits for the redirected output pipes to reach EOF, which never
                // happens while a detached child holds them open (e.g. "cmd /c start /b ...").
                await exitTcs.Task.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                timedOut = !process.HasExited;

                if (!process.HasExited)
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch { }
                }

                try { process.WaitForExit(5000); } catch { }
            }

            if (captureOutput)
            {
                // Give the redirected streams a short grace period to flush buffered
                // output. If a lingering child still holds the pipe open, this times
                // out instead of hanging until the overall timeout.
                using var drainCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try { await process.WaitForExitAsync(drainCts.Token); }
                catch (OperationCanceledException) { pipeHeldOpen = true; }
            }

            stopwatch.Stop();

            if (outputDropped)
            {
                outputBuilder.AppendLine("... [capture limit reached; subsequent output was discarded] ...");
            }

            if (errorDropped)
            {
                errorBuilder.AppendLine("... [capture limit reached; subsequent error output was discarded] ...");
            }

            if (!timedOut && pipeHeldOpen)
            {
                outputBuilder.AppendLine("... [a child process is still holding the output stream; output may be incomplete and background children may still be running] ...");
            }

            int exitCode;
            try { exitCode = process.HasExited ? process.ExitCode : -1; } catch { exitCode = -1; }

            return new CommandResult
            {
                ExitCode = exitCode,
                StandardOutput = outputBuilder.ToString(),
                StandardError = errorBuilder.ToString(),
                Duration = stopwatch.Elapsed,
                Command = command,
                WorkingDirectory = workingDirectory,
                TimedOut = timedOut
            };
        }

        private ToolResult StartBackgroundCommand(string command, string workingDirectory, bool runAsShell)
        {
            var processInfo = CreateProcessStartInfo(command, workingDirectory, captureOutput: true, runAsShell);
            var process = new Process { StartInfo = processInfo, EnableRaisingEvents = true };
            var bg = BackgroundCommandManager.Instance.Register(command, workingDirectory, process);

            process.OutputDataReceived += (sender, e) => { if (e.Data != null) bg.AppendStdout(e.Data); };
            process.ErrorDataReceived += (sender, e) => { if (e.Data != null) bg.AppendStderr(e.Data); };
            process.Exited += (sender, e) =>
            {
                int? exitCode = null;
                try { exitCode = process.ExitCode; } catch { }
                bg.MarkExited(exitCode);
            };

            try
            {
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            }
            catch
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                BackgroundCommandManager.Instance.Remove(bg.Id);
                throw;
            }

            var message =
                $"Command started in background with id '{bg.Id}'.\n" +
                $"Read its output with get_command_output (command_id: '{bg.Id}') and stop it with kill_command.";

            return CreateSuccessResult(
                new Dictionary<string, object>
                {
                    ["command_id"] = bg.Id,
                    ["status"] = "running",
                    ["command"] = command
                },
                message);
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
                    startInfo.ArgumentList.Add("-c");
                    startInfo.ArgumentList.Add(command);
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
            if (result.TimedOut)
            {
                output.AppendLine($"Status: TIMED OUT after {result.Duration.TotalSeconds:F1}s and was terminated (partial output below)");
            }
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

            var headLength = MaxOutputLength * 2 / 3;
            var tailLength = MaxOutputLength - headLength;
            var elided = output.Length - headLength - tailLength;

            return output.Substring(0, headLength)
                + $"\n\n... ({elided} characters elided) ...\n\n"
                + output.Substring(output.Length - tailLength);
        }

        public IReadOnlyList<CommandHistory> GetCommandHistory()
        {
            lock (_historyLock)
            {
                return _commandHistory.ToList();
            }
        }

        public void ClearHistory()
        {
            lock (_historyLock)
            {
                _commandHistory.Clear();
            }
        }
    }
}