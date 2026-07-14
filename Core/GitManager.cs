using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
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
            gitignoreContent.AppendLine("");
            gitignoreContent.AppendLine("# Windows special files");
            gitignoreContent.AppendLine("nul");
            gitignoreContent.AppendLine("NUL");

            await File.WriteAllTextAsync(gitignorePath, gitignoreContent.ToString());
            return true;
        }

        private static async Task<(bool success, string output)> CreateInitialCommit(string path)
        {
            // --ignore-errors stages everything it can instead of aborting on the
            // first un-addable file (reserved Windows names, locked files, etc.),
            // but git still exits non-zero when files were skipped.
            var addResult = await ExecuteGitCommand("add . --ignore-errors", path);
            if (!addResult.success)
            {
                var stagedResult = await ExecuteGitCommand("ls-files", path);
                if (stagedResult.success && string.IsNullOrWhiteSpace(stagedResult.output))
                {
                    return (false, $"Failed to add files: {addResult.output}");
                }
            }

            return await ExecuteGitCommand("commit --allow-empty -m \"Initial commit - Saturn workspace initialized\"", path);
        }

        private static async Task<(bool success, string output)> ExecuteGitCommand(string arguments, string? workingDirectory = null)
        {
            try
            {
                using var process = new Process
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

                // Drain both pipes concurrently; reading them sequentially can deadlock
                // when the process fills the other pipe's buffer and blocks.
                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();

                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                try
                {
                    await process.WaitForExitAsync(timeout.Token);
                }
                catch (OperationCanceledException)
                {
                    var terminated = false;
                    try
                    {
                        process.Kill(entireProcessTree: true);
                        terminated = process.WaitForExit(5000);
                    }
                    catch
                    {
                        terminated = process.HasExited;
                    }

                    if (terminated)
                    {
                        await Task.WhenAll(outputTask, errorTask);
                    }

                    return (false, terminated
                        ? "git command timed out after 10 seconds"
                        : "git command timed out after 10 seconds and could not be terminated; it may still hold repository locks");
                }

                var output = await outputTask;
                var error = await errorTask;

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