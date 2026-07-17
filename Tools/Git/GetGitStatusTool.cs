using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Saturn.Tools.Core;

namespace Saturn.Tools.Git
{
    public class GetGitStatusTool : ToolBase
    {
        public override string Name => "get_git_status";
        public override string Description => "Returns a structured list of modified, added, and untracked files in the current git repository.";

        protected override Dictionary<string, object> GetParameterProperties()
        {
            return new Dictionary<string, object>
            {
                {
                    "workingDirectory", new Dictionary<string, object>
                    {
                        { "type", "string" },
                        { "description", "The working directory to run git status in. Defaults to current directory." }
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
            var workingDirectory = GetParameter<string>(parameters, "workingDirectory", Environment.CurrentDirectory);

            if (!Directory.Exists(workingDirectory))
            {
                return CreateErrorResult($"Working directory does not exist: {workingDirectory}");
            }

            try
            {
                var result = await GitHelper.RunGitCommandAsync(new[] { "status", "--porcelain" }, workingDirectory);

                if (!result.Success)
                {
                    return CreateErrorResult($"Git status failed. Error: {result.Error}");
                }

                var lines = result.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

                var statusList = new List<GitFileStatus>();
                foreach (var line in lines)
                {
                    if (line.Length >= 4)
                    {
                        var status = line.Substring(0, 2);
                        var filePath = line.Substring(3);
                        statusList.Add(new GitFileStatus { Status = status, FilePath = filePath });
                    }
                }

                var formattedOutput = string.Join(Environment.NewLine, statusList.Select(s => $"[{s.Status}] {s.FilePath}"));
                if (string.IsNullOrEmpty(formattedOutput))
                {
                    formattedOutput = "Working tree clean.";
                }

                return CreateSuccessResult(statusList, formattedOutput);
            }
            catch (Exception ex)
            {
                return CreateErrorResult($"Exception executing git status: {ex.Message}");
            }
        }

        public class GitFileStatus
        {
            public string Status { get; set; } = string.Empty;
            public string FilePath { get; set; } = string.Empty;
        }
    }
}