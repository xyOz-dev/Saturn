using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Saturn.Tools.Git
{
    public static class GitHelper
    {
        public static async Task<(bool Success, string Output, string Error)> RunGitCommandAsync(IEnumerable<string> arguments, string workingDirectory, int timeoutSeconds = 30)
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (var arg in arguments)
            {
                processStartInfo.ArgumentList.Add(arg);
            }

            using var process = Process.Start(processStartInfo);
            if (process == null)
            {
                return (false, "", "Failed to start git process.");
            }

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Git spawns children (hooks, credential helpers); killing only the
                // parent leaves them orphaned and holding the index lock.
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // Already exited between the timeout and the kill.
                }
                return (false, "", $"Git command timed out after {timeoutSeconds}s.");
            }

            var output = await outputTask;
            var error = await errorTask;

            return (process.ExitCode == 0, output, error);
        }
    }
}