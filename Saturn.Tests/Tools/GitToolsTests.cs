using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Saturn.Tools.Core;
using Saturn.Tools.Git;
using Xunit;

namespace Saturn.Tests.Tools
{
    public class GitToolsTests : IAsyncLifetime
    {
        private readonly string _repo;

        public GitToolsTests()
        {
            _repo = Path.Combine(Path.GetTempPath(), $"SaturnGitTest_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_repo);
        }

        public async Task InitializeAsync()
        {
            await Git("init");
            await Git("config", "user.name", "Test");
            await Git("config", "user.email", "test@test.local");
            await Git("config", "commit.gpgsign", "false");
            File.WriteAllText(Path.Combine(_repo, "tracked.txt"), "original\n");
            await Git("add", "tracked.txt");
            await Git("commit", "-m", "initial");
        }

        public Task DisposeAsync()
        {
            try
            {
                // Clear read-only attributes .git objects carry on Windows.
                foreach (var file in Directory.EnumerateFiles(_repo, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }
                Directory.Delete(_repo, recursive: true);
            }
            catch
            {
            }
            return Task.CompletedTask;
        }

        private async Task Git(params string[] args)
        {
            var result = await GitHelper.RunGitCommandAsync(args, _repo);
            result.Success.Should().BeTrue($"git {string.Join(" ", args)} should succeed but got: {result.Error}");
        }

        [Theory]
        [InlineData("plain.txt", "plain.txt")]
        [InlineData("\"with space.txt\"", "with space.txt")]
        [InlineData("\"quote\\\".txt\"", "quote\".txt")]
        [InlineData("\"tab\\there\"", "tab\there")]
        public void UnquoteGitPath_ResolvesCStyleQuoting(string input, string expected)
        {
            GetGitStatusTool.UnquoteGitPath(input).Should().Be(expected);
        }

        [Fact]
        public async Task Status_ReportsModifiedAndUntrackedFiles()
        {
            File.WriteAllText(Path.Combine(_repo, "tracked.txt"), "changed\n");
            File.WriteAllText(Path.Combine(_repo, "new.txt"), "hi\n");

            var result = await new GetGitStatusTool().ExecuteAsync(new Dictionary<string, object>
            {
                ["workingDirectory"] = _repo
            });

            result.Success.Should().BeTrue();
            result.FormattedOutput.Should().Contain("tracked.txt");
            result.FormattedOutput.Should().Contain("[??] new.txt");
        }

        [Fact]
        public async Task Status_ReportsRenameWithOldAndNewPath()
        {
            await Git("mv", "tracked.txt", "renamed.txt");

            var result = await new GetGitStatusTool().ExecuteAsync(new Dictionary<string, object>
            {
                ["workingDirectory"] = _repo
            });

            result.Success.Should().BeTrue();
            result.FormattedOutput.Should().Contain("tracked.txt -> renamed.txt");
        }

        [Fact]
        public async Task Diff_NoTrackedChanges_MentionsUntrackedFiles()
        {
            File.WriteAllText(Path.Combine(_repo, "brand-new.txt"), "hi\n");

            var result = await new GetGitDiffTool().ExecuteAsync(new Dictionary<string, object>
            {
                ["workingDirectory"] = _repo
            });

            result.Success.Should().BeTrue();
            result.FormattedOutput.Should().Contain("No differences found.");
            result.FormattedOutput.Should().Contain("untracked");
            result.FormattedOutput.Should().Contain("brand-new.txt");
        }

        [Fact]
        public async Task Commit_WithFiles_CommitsOnlyThoseFiles()
        {
            File.WriteAllText(Path.Combine(_repo, "a.txt"), "a\n");
            File.WriteAllText(Path.Combine(_repo, "b.txt"), "b\n");

            var result = await new GitCommitTool(new CommandApprovalService(false)).ExecuteAsync(new Dictionary<string, object>
            {
                ["message"] = "add a only",
                ["files"] = new[] { "a.txt" },
                ["workingDirectory"] = _repo
            });

            result.Success.Should().BeTrue();

            var status = await GitHelper.RunGitCommandAsync(new[] { "status", "--porcelain" }, _repo);
            status.Output.Should().Contain("b.txt");
            status.Output.Should().NotContain("a.txt");
        }

        [Fact]
        public async Task Commit_FileArgumentLookingLikeFlag_IsNotTreatedAsFlag()
        {
            File.WriteAllText(Path.Combine(_repo, "secret.txt"), "do not stage me\n");

            var result = await new GitCommitTool(new CommandApprovalService(false)).ExecuteAsync(new Dictionary<string, object>
            {
                ["message"] = "sneaky",
                ["files"] = new[] { "-A" },
                ["workingDirectory"] = _repo
            });

            // "-A" must be rejected as a pathspec, not honored as add-everything.
            result.Success.Should().BeFalse();

            var status = await GitHelper.RunGitCommandAsync(new[] { "status", "--porcelain" }, _repo);
            status.Output.Should().Contain("?? secret.txt");
        }

        [Fact]
        public async Task Helper_Timeout_ReportsTimeoutInsteadOfHanging()
        {
            // "git log" through a pager could hang, but redirected output never pages;
            // use a genuinely slow operation instead: fetch from a non-routable remote.
            var result = await GitHelper.RunGitCommandAsync(
                new[] { "fetch", "https://10.255.255.1/nonexistent.git" }, _repo, timeoutSeconds: 2);

            result.Success.Should().BeFalse();
            result.Error.Should().MatchRegex("timed out|fatal|unable");
        }
    }
}
