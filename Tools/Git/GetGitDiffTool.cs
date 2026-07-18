using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Saturn.Tools.Core;

namespace Saturn.Tools.Git
{
    public class GetGitDiffTool : ToolBase
    {
        public override string Name => "get_git_diff";
        public override string Description => "Gets the current uncommitted changes in a structured format or raw diff.";

        protected override Dictionary<string, object> GetParameterProperties()
        {
            return new Dictionary<string, object>
            {
                {
                    "filePath", new Dictionary<string, object>
                    {
                        { "type", "string" },
                        { "description", "If provided, only get diff for this file." }
                    }
                },
                {
                    "staged", new Dictionary<string, object>
                    {
                        { "type", "boolean" },
                        { "description", "If true, get diff of staged changes (git diff --cached)." }
                    }
                },
                {
                    "statOnly", new Dictionary<string, object>
                    {
                        { "type", "boolean" },
                        { "description", "If true, only return the diff stat (summary) instead of the full diff." }
                    }
                },
                {
                    "workingDirectory", new Dictionary<string, object>
                    {
                        { "type", "string" },
                        { "description", "The working directory to run git diff in. Defaults to current directory." }
                    }
                }
            };
        }

        protected override string[] GetRequiredParameters()
        {
            return Array.Empty<string>();
        }

        public override async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters)
        {
            var filePath = GetParameter<string>(parameters, "filePath", string.Empty);
            var staged = GetParameter<bool>(parameters, "staged", false);
            var statOnly = GetParameter<bool>(parameters, "statOnly", false);
            var workingDirectory = GetParameter<string>(parameters, "workingDirectory", Saturn.Core.Workspace.WorkspaceManager.CurrentWorkspace);

            if (!Directory.Exists(workingDirectory))
            {
                return CreateErrorResult($"Working directory does not exist: {workingDirectory}");
            }

            try
            {
                var arguments = new List<string> { "diff" };
                
                if (staged)
                {
                    arguments.Add("--cached");
                }
                
                if (statOnly)
                {
                    arguments.Add("--stat");
                }
                
                if (!string.IsNullOrEmpty(filePath))
                {
                    arguments.Add("--");
                    arguments.Add(filePath);
                }

                var result = await GitHelper.RunGitCommandAsync(arguments, workingDirectory);

                if (!result.Success)
                {
                    return CreateErrorResult($"Git diff failed. Error: {result.Error}");
                }

                var output = result.Output;

                if (string.IsNullOrEmpty(output))
                {
                    // Plain "git diff" cannot see untracked files; without this hint an
                    // agent that just created new files concludes nothing has changed.
                    var untrackedNote = staged ? "" : await DescribeUntrackedFilesAsync(workingDirectory, filePath);
                    return CreateSuccessResult(new { Diff = "" }, $"No differences found.{untrackedNote}");
                }

                // Truncate if it's too large and not just a stat
                if (!statOnly && output.Length > 50000)
                {
                    var cut = 50000;
                    // Never split a surrogate pair at the truncation boundary.
                    if (char.IsHighSurrogate(output[cut - 1]))
                    {
                        cut--;
                    }
                    output = output.Substring(0, cut) + "\n\n... [Diff truncated due to length. Use statOnly=true for a summary.]";
                }

                return CreateSuccessResult(new { Diff = output }, output);
            }
            catch (Exception ex)
            {
                return CreateErrorResult($"Exception executing git diff: {ex.Message}");
            }
        }

        private static async Task<string> DescribeUntrackedFilesAsync(string workingDirectory, string filePath)
        {
            var args = new List<string> { "ls-files", "--others", "--exclude-standard" };
            if (!string.IsNullOrEmpty(filePath))
            {
                args.Add("--");
                args.Add(filePath);
            }

            var result = await GitHelper.RunGitCommandAsync(args, workingDirectory);
            if (!result.Success)
            {
                return "";
            }

            var untracked = result.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (untracked.Length == 0)
            {
                return "";
            }

            var listed = string.Join(", ", untracked.Take(10));
            var suffix = untracked.Length > 10 ? $" (and {untracked.Length - 10} more)" : "";
            return $" Note: {untracked.Length} untracked file(s) are not shown by git diff: {listed}{suffix}";
        }
    }
}