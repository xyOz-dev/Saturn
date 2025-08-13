using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Saturn.Core
{
    public static class GitManager
    {
        public static bool IsGitInstalled()
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "git",
                        Arguments = "--version",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                process.WaitForExit(5000);
                return process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        public static bool IsRepository(string? path = null)
        {
            path ??= Environment.CurrentDirectory;
            return Directory.Exists(Path.Combine(path, ".git"));
        }

        public static async Task<(bool success, string message)> InitializeRepository(string? path = null)
        {
            path ??= Environment.CurrentDirectory;

            if (!IsGitInstalled())
            {
                return (false, "Git is not installed. Please install Git from https://git-scm.com/downloads");
            }

            if (IsRepository(path))
            {
                return (true, "Repository already exists");
            }

            try
            {
                var initResult = await ExecuteGitCommand("init", path);
                if (!initResult.success)
                {
                    return (false, $"Failed to initialize repository: {initResult.output}");
                }

                var hasUserConfig = await CheckUserConfig();
                if (!hasUserConfig)
                {
                    await ConfigureDefaultUser();
                }

                var gitignoreCreated = await CreateGitignore(path);

                var commitResult = await CreateInitialCommit(path);
                if (!commitResult.success)
                {
                    return (false, $"Repository initialized but initial commit failed: {commitResult.output}");
                }

                return (true, "Repository initialized successfully with initial commit");
            }
            catch (Exception ex)
            {
                return (false, $"Failed to initialize repository: {ex.Message}");
            }
        }

        private static async Task<bool> CheckUserConfig()
        {
            var nameResult = await ExecuteGitCommand("config user.name");
            var emailResult = await ExecuteGitCommand("config user.email");
            
            return nameResult.success && !string.IsNullOrWhiteSpace(nameResult.output) &&
                   emailResult.success && !string.IsNullOrWhiteSpace(emailResult.output);
        }

        private static async Task ConfigureDefaultUser()
        {
            await ExecuteGitCommand("config user.name \"Saturn User\"");
            await ExecuteGitCommand("config user.email \"saturn@localhost\"");
        }

        private static async Task<bool> CreateGitignore(string path)
        {
            var gitignorePath = Path.Combine(path, ".gitignore");
            
            if (File.Exists(gitignorePath))
                return false;

            var gitignoreContent = new StringBuilder();
            gitignoreContent.AppendLine("# .NET");
            gitignoreContent.AppendLine("bin/");
            gitignoreContent.AppendLine("obj/");
            gitignoreContent.AppendLine("*.user");
            gitignoreContent.AppendLine("*.suo");
            gitignoreContent.AppendLine(".vs/");
            gitignoreContent.AppendLine("");
            gitignoreContent.AppendLine("# Build results");
            gitignoreContent.AppendLine("[Dd]ebug/");
            gitignoreContent.AppendLine("[Rr]elease/");
            gitignoreContent.AppendLine("x64/");
            gitignoreContent.AppendLine("x86/");
            gitignoreContent.AppendLine("[Bb]uild/");
            gitignoreContent.AppendLine("");
            gitignoreContent.AppendLine("# NuGet");
            gitignoreContent.AppendLine("*.nupkg");
            gitignoreContent.AppendLine("packages/");
            gitignoreContent.AppendLine("");
            gitignoreContent.AppendLine("# Logs");
            gitignoreContent.AppendLine("*.log");
            gitignoreContent.AppendLine("");
            gitignoreContent.AppendLine("# OS files");
            gitignoreContent.AppendLine(".DS_Store");
            gitignoreContent.AppendLine("Thumbs.db");

            await File.WriteAllTextAsync(gitignorePath, gitignoreContent.ToString());
            return true;
        }

        private static async Task<(bool success, string output)> CreateInitialCommit(string path)
        {
            var addResult = await ExecuteGitCommand("add .", path);
            if (!addResult.success)
            {
                return addResult;
            }

            return await ExecuteGitCommand("commit -m \"Initial commit - Saturn workspace initialized\"", path);
        }

        private static async Task<(bool success, string output)> ExecuteGitCommand(string arguments, string? workingDirectory = null)
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "git",
                        Arguments = arguments,
                        WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                
                var output = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();
                
                await Task.Run(() => process.WaitForExit(10000));

                if (process.ExitCode == 0)
                {
                    return (true, output);
                }
                else
                {
                    return (false, string.IsNullOrWhiteSpace(error) ? output : error);
                }
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }
    }
}