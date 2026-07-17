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
                // quotepath=off keeps non-ASCII paths readable instead of octal-escaped.
                var result = await GitHelper.RunGitCommandAsync(
                    new[] { "-c", "core.quotepath=off", "status", "--porcelain" }, workingDirectory);

                if (!result.Success)
                {
                    return CreateErrorResult($"Git status failed. Error: {result.Error}");
                }

                var lines = result.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

                var statusList = new List<GitFileStatus>();
                foreach (var line in lines)
                {
                    if (line.Length < 4)
                    {
                        continue;
                    }

                    var status = line.Substring(0, 2);
                    var pathPart = line.Substring(3);
                    string? oldPath = null;

                    // Renames and copies come through as "old -> new".
                    if (status[0] == 'R' || status[0] == 'C')
                    {
                        var arrow = pathPart.IndexOf(" -> ", StringComparison.Ordinal);
                        if (arrow >= 0)
                        {
                            oldPath = UnquoteGitPath(pathPart.Substring(0, arrow));
                            pathPart = pathPart.Substring(arrow + 4);
                        }
                    }

                    statusList.Add(new GitFileStatus
                    {
                        Status = status,
                        FilePath = UnquoteGitPath(pathPart),
                        OldPath = oldPath
                    });
                }

                var formattedOutput = string.Join(Environment.NewLine,
                    statusList.Select(s => s.OldPath != null
                        ? $"[{s.Status}] {s.OldPath} -> {s.FilePath}"
                        : $"[{s.Status}] {s.FilePath}"));
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

        // Paths with special characters (quotes, newlines) arrive C-style quoted
        // even with quotepath=off; strip the quotes and resolve the escapes.
        internal static string UnquoteGitPath(string path)
        {
            if (path.Length < 2 || path[0] != '"' || path[path.Length - 1] != '"')
            {
                return path;
            }

            var inner = path.Substring(1, path.Length - 2);
            var sb = new System.Text.StringBuilder(inner.Length);
            for (var i = 0; i < inner.Length; i++)
            {
                var c = inner[i];
                if (c != '\\' || i + 1 >= inner.Length)
                {
                    sb.Append(c);
                    continue;
                }

                var next = inner[++i];
                sb.Append(next switch
                {
                    'n' => '\n',
                    't' => '\t',
                    'r' => '\r',
                    _ => next
                });
            }
            return sb.ToString();
        }

        public class GitFileStatus
        {
            public string Status { get; set; } = string.Empty;
            public string FilePath { get; set; } = string.Empty;
            public string? OldPath { get; set; }
        }
    }
}