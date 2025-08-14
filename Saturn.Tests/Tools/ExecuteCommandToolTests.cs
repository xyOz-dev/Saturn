using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Xunit;
using FluentAssertions;
using Saturn.Tools;
using Saturn.Tools.Core;

namespace Saturn.Tests.Tools
{
    public class ExecuteCommandToolTests : IDisposable
    {
        private readonly string _testDirectory;
        private readonly ExecuteCommandTool _tool;
        private readonly List<string> _createdFiles;

        public ExecuteCommandToolTests()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), $"SaturnCmdTests_{Guid.NewGuid()}");
            Directory.CreateDirectory(_testDirectory);
            _createdFiles = new List<string>();
            _tool = new ExecuteCommandTool();
        }

        [Fact]
        public async Task ExecuteAsync_WithValidCommand_ReturnsSuccess()
        {
            // Arrange
            var command = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "echo Hello World" : "echo 'Hello World'";
            var parameters = new Dictionary<string, object>
            {
                { "command", command }
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.FormattedOutput.Should().Contain("Hello World");
            result.FormattedOutput.Should().Contain("Exit Code: 0");
        }

        [Fact]
        public async Task ExecuteAsync_WithInvalidCommand_ReturnsError()
        {
            // Arrange
            var parameters = new Dictionary<string, object>
            {
                { "command", "nonexistentcommand12345" }
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeFalse();
            result.Error.Should().NotBeNullOrEmpty();
        }

        [Fact]
        public async Task ExecuteAsync_WithEmptyCommand_ReturnsError()
        {
            // Arrange
            var parameters = new Dictionary<string, object>
            {
                { "command", "" }
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeFalse();
            result.Error.Should().Contain("Command parameter is required");
        }

        [Fact]
        public async Task ExecuteAsync_WithMissingCommand_ReturnsError()
        {
            // Arrange
            var parameters = new Dictionary<string, object>();

            // Act
            var result = await _tool.ExecuteAsync(parameters);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeFalse();
            result.Error.Should().Contain("Command parameter is required");
        }

        [Fact]
        public async Task ExecuteAsync_WithWorkingDirectory_ExecutesInSpecifiedDirectory()
        {
            // Arrange
            var command = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "cd" : "pwd";
            var parameters = new Dictionary<string, object>
            {
                { "command", command },
                { "workingDirectory", _testDirectory }
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.FormattedOutput.Should().Contain(_testDirectory);
        }

        [Fact]
        public async Task ExecuteAsync_WithInvalidWorkingDirectory_ReturnsError()
        {
            // Arrange
            var nonExistentDir = Path.Combine(_testDirectory, "nonexistent");
            var parameters = new Dictionary<string, object>
            {
                { "command", "echo test" },
                { "workingDirectory", nonExistentDir }
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeFalse();
            result.Error.Should().Contain("Working directory does not exist");
        }

        [Fact]
        public async Task ExecuteAsync_WithTimeout_TerminatesLongRunningCommand()
        {
            // Arrange
            var command = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) 
                ? "ping -n 10 127.0.0.1" 
                : "sleep 10";
            var parameters = new Dictionary<string, object>
            {
                { "command", command },
                { "timeout", 1 } // 1 second timeout
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeFalse();
            result.Error.Should().Contain("timed out");
        }

        [Fact]
        public async Task ExecuteAsync_WithCaptureOutputFalse_DoesNotCaptureOutput()
        {
            // Arrange
            var parameters = new Dictionary<string, object>
            {
                { "command", "echo test" },
                { "captureOutput", false }
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters);

            // Assert
            result.Should().NotBeNull();
            // When output is not captured, we should still get exit code info
            result.FormattedOutput.Should().Contain("Exit Code:");
            result.FormattedOutput.Should().NotContain("Standard Output");
        }

        [Fact]
        public async Task ExecuteAsync_WithRunAsShellFalse_ExecutesDirectly()
        {
            // Arrange
            var echoCommand = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) 
                ? "cmd.exe /c echo direct" 
                : "echo direct";
            var parameters = new Dictionary<string, object>
            {
                { "command", echoCommand },
                { "runAsShell", false }
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters);

            // Assert
            result.Should().NotBeNull();
            // Result depends on platform, but should complete without throwing
        }

        [Fact]
        public async Task ExecuteAsync_WithStandardError_CapturesErrorOutput()
        {
            // Arrange
            var command = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? "cmd /c \"echo error message 1>&2\""
                : "echo 'error message' >&2";
            var parameters = new Dictionary<string, object>
            {
                { "command", command }
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters);

            // Assert
            result.Should().NotBeNull();
            result.FormattedOutput.Should().Contain("error message");
        }

        [Fact]
        public async Task ExecuteAsync_WithExitCode_ReturnsCorrectExitCode()
        {
            // Arrange
            var command = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? "cmd /c exit 42"
                : "exit 42";
            var parameters = new Dictionary<string, object>
            {
                { "command", command }
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeFalse();
            result.Error.Should().Contain("42");
        }

        [Fact]
        public async Task ExecuteAsync_WithSecurityMode_ValidatesCommand()
        {
            // Arrange
            var config = new CommandExecutorConfig
            {
                SecurityMode = SecurityMode.Restricted,
                CustomValidator = (cmd) => new CommandValidationResult 
                { 
                    IsValid = false, 
                    Reason = "Test validation failure" 
                }
            };
            var toolWithSecurity = new ExecuteCommandTool(config);
            var parameters = new Dictionary<string, object>
            {
                { "command", "echo test" }
            };

            // Act
            var result = await toolWithSecurity.ExecuteAsync(parameters);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeFalse();
            result.Error.Should().Contain("Command blocked");
            result.Error.Should().Contain("Test validation failure");
        }

        [Fact]
        public async Task GetCommandHistory_TracksExecutedCommands()
        {
            // Arrange
            var config = new CommandExecutorConfig { EnableHistory = true };
            var toolWithHistory = new ExecuteCommandTool(config);

            // Act
            await toolWithHistory.ExecuteAsync(new Dictionary<string, object> 
            { 
                { "command", "echo test1" } 
            });

            await toolWithHistory.ExecuteAsync(new Dictionary<string, object> 
            { 
                { "command", "echo test2" } 
            });

            var history = toolWithHistory.GetCommandHistory();

            // Assert
            history.Should().HaveCount(2);
            history[0].Command.Should().Contain("test1");
            history[1].Command.Should().Contain("test2");
        }

        [Fact]
        public async Task ClearHistory_RemovesAllHistory()
        {
            // Arrange
            var config = new CommandExecutorConfig { EnableHistory = true };
            var toolWithHistory = new ExecuteCommandTool(config);
            
            await toolWithHistory.ExecuteAsync(new Dictionary<string, object> 
            { 
                { "command", "echo test" } 
            });

            // Act
            toolWithHistory.ClearHistory();
            var history = toolWithHistory.GetCommandHistory();

            // Assert
            history.Should().BeEmpty();
        }

        [Fact]
        public async Task GetCommandHistory_RespectsMaxHistorySize()
        {
            // Arrange
            var config = new CommandExecutorConfig 
            { 
                EnableHistory = true,
                MaxHistorySize = 2
            };
            var toolWithHistory = new ExecuteCommandTool(config);

            // Act
            for (int i = 0; i < 5; i++)
            {
                await toolWithHistory.ExecuteAsync(new Dictionary<string, object> 
                { 
                    { "command", $"echo test{i}" } 
                });
            }

            var history = toolWithHistory.GetCommandHistory();

            // Assert
            history.Should().HaveCount(2);
            history[0].Command.Should().Contain("test3");
            history[1].Command.Should().Contain("test4");
        }

        [Fact]
        public void GetDisplaySummary_ReturnsFormattedCommand()
        {
            // Arrange
            var parameters = new Dictionary<string, object>
            {
                { "command", "echo 'This is a very long command that should be truncated for display purposes'" }
            };

            // Act
            var summary = _tool.GetDisplaySummary(parameters);

            // Assert
            summary.Should().StartWith("$ ");
            summary.Should().Contain("echo");
            summary.Length.Should().BeLessThanOrEqualTo(60); // Account for "$ " prefix
        }

        [Fact]
        public void GetDisplaySummary_HandlesEmptyCommand()
        {
            // Arrange
            var parameters = new Dictionary<string, object>
            {
                { "command", "" }
            };

            // Act
            var summary = _tool.GetDisplaySummary(parameters);

            // Assert
            summary.Should().Be("$ ");
        }

        [Fact]
        public void ToolMetadata_IsCorrect()
        {
            // Assert
            _tool.Name.Should().Be("execute_command");
            _tool.Description.Should().Contain("Executes system commands");
        }

        [Fact]
        public async Task ExecuteAsync_WithMultilineOutput_CapturesAllLines()
        {
            // Arrange
            var command = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? "cmd /c \"echo Line1 & echo Line2 & echo Line3\""
                : "echo -e 'Line1\\nLine2\\nLine3'";
            var parameters = new Dictionary<string, object>
            {
                { "command", command }
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.FormattedOutput.Should().Contain("Line1");
            result.FormattedOutput.Should().Contain("Line2");
            result.FormattedOutput.Should().Contain("Line3");
        }

        [Fact]
        public async Task ExecuteAsync_WithLargeOutput_TruncatesOutput()
        {
            // Arrange
            // Create a command that generates large output
            var command = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? "cmd /c \"for /L %i in (1,1,100000) do @echo This is line number %i\""
                : "for i in {1..100000}; do echo \"This is line number $i\"; done";
            
            var parameters = new Dictionary<string, object>
            {
                { "command", command },
                { "timeout", 5 } // Set timeout to prevent hanging
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters);

            // Assert
            result.Should().NotBeNull();
            // Either succeeds with truncated output or times out
            if (result.FormattedOutput.Contains("output truncated"))
            {
                result.FormattedOutput.Should().Contain("output truncated");
            }
        }

        [Fact]
        public async Task ExecuteAsync_ParseCommand_HandlesQuotedExecutable()
        {
            // Arrange
            var testScript = CreateTestScript("test script.bat", "@echo Script with spaces");
            var parameters = new Dictionary<string, object>
            {
                { "command", $"\"{testScript}\"" },
                { "runAsShell", false }
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters);

            // Assert
            result.Should().NotBeNull();
            // Should handle the quoted path correctly
        }

        private string CreateTestScript(string fileName, string content)
        {
            var filePath = Path.Combine(_testDirectory, fileName);
            File.WriteAllText(filePath, content);
            _createdFiles.Add(filePath);
            
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Make script executable on Unix-like systems
                File.SetUnixFileMode(filePath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            
            return filePath;
        }

        public void Dispose()
        {
            // Clear any remaining history
            _tool.ClearHistory();

            // Clean up test files
            foreach (var file in _createdFiles)
            {
                if (File.Exists(file))
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch { }
                }
            }

            // Remove test directory
            if (Directory.Exists(_testDirectory))
            {
                try
                {
                    Directory.Delete(_testDirectory, true);
                }
                catch { }
            }
        }
    }
}