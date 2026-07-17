using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using FluentAssertions;
using Saturn.Tools.Git;
using Saturn.Tools.Core;

namespace Saturn.Tests.Tools
{
    public class GitCommitToolTests : IDisposable
    {
        private sealed class FakeApprovalService : ICommandApprovalService
        {
            public bool Approve;
            public string? LastCommand;
            public string? LastWorkingDirectory;

            public Task<bool> RequestApprovalAsync(string command, string workingDirectory)
            {
                LastCommand = command;
                LastWorkingDirectory = workingDirectory;
                return Task.FromResult(Approve);
            }
        }

        private readonly string _repoPath;

        public GitCommitToolTests()
        {
            _repoPath = Path.Combine(Path.GetTempPath(), "saturn-git-commit-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_repoPath);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_repoPath, recursive: true);
            }
            catch { }
        }

        private async Task InitRepoAsync()
        {
            (await GitHelper.RunGitCommandAsync(new[] { "init" }, _repoPath)).Success.Should().BeTrue();
            (await GitHelper.RunGitCommandAsync(new[] { "config", "user.email", "test@example.com" }, _repoPath)).Success.Should().BeTrue();
            (await GitHelper.RunGitCommandAsync(new[] { "config", "user.name", "Test" }, _repoPath)).Success.Should().BeTrue();
        }

        [Fact]
        public async Task DeniedApproval_ReturnsErrorAndDoesNotCommit()
        {
            await InitRepoAsync();
            await File.WriteAllTextAsync(Path.Combine(_repoPath, "file.txt"), "content");

            var approval = new FakeApprovalService { Approve = false };
            var tool = new GitCommitTool(approval);

            var result = await tool.ExecuteAsync(new Dictionary<string, object>
            {
                ["message"] = "should not land",
                ["files"] = new[] { "file.txt" },
                ["workingDirectory"] = _repoPath
            });

            result.Success.Should().BeFalse();
            result.Error.Should().Contain("denied");
            approval.LastCommand.Should().Contain("git add file.txt");
            approval.LastCommand.Should().Contain("git commit");
            approval.LastWorkingDirectory.Should().Be(_repoPath);

            var log = await GitHelper.RunGitCommandAsync(new[] { "log", "--oneline" }, _repoPath);
            log.Success.Should().BeFalse("no commit should exist in the repo");
        }

        [Fact]
        public async Task ApprovedCommit_Succeeds()
        {
            await InitRepoAsync();
            await File.WriteAllTextAsync(Path.Combine(_repoPath, "file.txt"), "content");

            var approval = new FakeApprovalService { Approve = true };
            var tool = new GitCommitTool(approval);

            var result = await tool.ExecuteAsync(new Dictionary<string, object>
            {
                ["message"] = "test commit",
                ["files"] = new[] { "file.txt" },
                ["workingDirectory"] = _repoPath
            });

            result.Success.Should().BeTrue();

            var log = await GitHelper.RunGitCommandAsync(new[] { "log", "--oneline" }, _repoPath);
            log.Success.Should().BeTrue();
            log.Output.Should().Contain("test commit");
        }
    }
}
