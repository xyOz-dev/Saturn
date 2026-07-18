using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Saturn.Tools.Core;

namespace Saturn.Tools.Git
{
    public class GitCommitTool : ToolBase
    {
        private readonly ICommandApprovalService _approvalService;

        public GitCommitTool() : this(null!) { }

        public GitCommitTool(ICommandApprovalService approvalService)
        {
            _approvalService = approvalService ?? new CommandApprovalService(true);
        }

        public override string Name => "git_commit";
        public override string Description => "Creates a new git commit with the specified message. The user may be asked to approve the commit before it runs; a denied commit returns an error.";

        protected override Dictionary<string, object> GetParameterProperties()
        {
            return new Dictionary<string, object>
            {
                {
                    "message", new Dictionary<string, object>
                    {
                        { "type", "string" },
                        { "description", "The commit message." }
                    }
                },
                {
                    "files", new Dictionary<string, object>
                    {
                        { "type", "array" },
                        { "items", new Dictionary<string, object> { { "type", "string" } } },
                        { "description", "List of specific files to add before committing. If empty, only already staged changes are committed." }
                    }
                },
                {
                    "workingDirectory", new Dictionary<string, object>
                    {
                        { "type", "string" },
                        { "description", "The working directory to run git commit in. Defaults to current directory." }
                    }
                }
            };
        }

        protected override string[] GetRequiredParameters()
        {
            return new[] { "message" };
        }

        public override async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters)
        {
            var message = GetParameter<string>(parameters, "message");
            var files = GetParameter<string[]>(parameters, "files", Array.Empty<string>());
            var workingDirectory = GetParameter<string>(parameters, "workingDirectory", Saturn.Core.Workspace.WorkspaceManager.CurrentWorkspace);

            if (string.IsNullOrEmpty(message))
            {
                return CreateErrorResult("Commit message is required.");
            }

            if (!Directory.Exists(workingDirectory))
            {
                return CreateErrorResult($"Working directory does not exist: {workingDirectory}");
            }

            if (AgentContext.RequireCommandApproval)
            {
                var approved = await _approvalService.RequestApprovalAsync(
                    DescribeCommand(message, files), workingDirectory);
                if (!approved)
                {
                    return CreateErrorResult("Git commit denied by user");
                }
            }

            try
            {
                if (files != null && files.Length > 0)
                {
                    // "--" stops flag parsing so a "file" like -A cannot silently
                    // re-enable the add-everything behavior this parameter prevents.
                    var addArgs = new List<string> { "add", "--" };
                    addArgs.AddRange(files);
                    
                    var addResult = await GitHelper.RunGitCommandAsync(addArgs, workingDirectory);
                    if (!addResult.Success)
                    {
                        return CreateErrorResult($"Failed to add files: {addResult.Error}");
                    }
                }

                var commitArgs = new List<string> { "commit", "-m", message };
                var commitResult = await GitHelper.RunGitCommandAsync(commitArgs, workingDirectory);
                
                if (!commitResult.Success)
                {
                    return CreateErrorResult($"Git commit failed: {commitResult.Error}");
                }

                return CreateSuccessResult(new { Output = commitResult.Output }, commitResult.Output);
            }
            catch (Exception ex)
            {
                return CreateErrorResult($"Exception executing git commit: {ex.Message}");
            }
        }

        private static string DescribeCommand(string message, string[]? files)
        {
            var commit = $"git commit -m \"{message}\"";
            if (files == null || files.Length == 0)
            {
                return commit;
            }
            return $"git add {string.Join(" ", files)} && {commit}";
        }
    }
}