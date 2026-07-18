using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Saturn.Core.Workspace;
using Saturn.Data;
using Saturn.Data.Tasks;
using Saturn.Tests.TestHelpers;
using Saturn.Tools;
using Xunit;

namespace Saturn.Tests.Core
{
    [Collection("WorkingDirectory")]
    public class WorkspaceSwitchFlowTests : IDisposable
    {
        private readonly FileTestHelper _files = new();
        private readonly string _globalDir;

        public WorkspaceSwitchFlowTests()
        {
            _globalDir = _files.CreateDirectory("global");
        }

        public void Dispose()
        {
            WorkspaceManager.ResetForTests();
            _files.Dispose();
        }

        [Fact]
        public void ChatHistoryRepository_AfterSwitch_CreatesDbInNewWorkspace()
        {
            var wsA = _files.CreateDirectory("ws-a");
            var wsB = _files.CreateDirectory("ws-b");

            WorkspaceManager.TrySwitch(wsA).Success.Should().BeTrue();
            using (var repoA = new ChatHistoryRepository())
            {
                File.Exists(Path.Combine(wsA, ".saturn", "chats.db")).Should().BeTrue();
            }

            WorkspaceManager.TrySwitch(wsB).Success.Should().BeTrue();
            using (var repoB = new ChatHistoryRepository())
            {
                File.Exists(Path.Combine(wsB, ".saturn", "chats.db")).Should().BeTrue();
            }
        }

        [Fact]
        public async Task TaskStore_SwitchWorkspace_RepointsProjectDbAndKeepsSubscribers()
        {
            var wsA = _files.CreateDirectory("tasks-a");
            var wsB = _files.CreateDirectory("tasks-b");

            using var store = new TaskStore(wsA, _globalDir);
            var changes = new List<string>();
            store.OnTaskChanged += (change, _) => changes.Add(change);

            await store.CreateAsync(new TaskCreateSpec { Title = "in A" });
            File.Exists(Path.Combine(wsA, ".saturn", "tasks.db")).Should().BeTrue();

            store.SwitchWorkspace(wsB);

            var tasksInB = await store.ListAsync();
            tasksInB.Should().BeEmpty("workspace B starts with a fresh tasks.db");

            await store.CreateAsync(new TaskCreateSpec { Title = "in B" });
            File.Exists(Path.Combine(wsB, ".saturn", "tasks.db")).Should().BeTrue();
            changes.Should().HaveCount(2, "OnTaskChanged subscribers survive the switch");

            // Workspace A's data is untouched by work done in B.
            using var reopenedA = new TaskStore(wsA, _globalDir);
            var tasksInA = await reopenedA.ListAsync();
            tasksInA.Should().ContainSingle(v => v.Task.Title == "in A");
        }

        [Fact]
        public async Task ReadFileTool_SandboxFollowsWorkspaceNotProcessCwd()
        {
            var workspace = _files.CreateDirectory("sandbox-ws");
            var outside = _files.CreateDirectory("outside");
            var insideFile = Path.Combine(workspace, "inside.txt");
            var outsideFile = Path.Combine(outside, "outside.txt");
            File.WriteAllText(insideFile, "inside content");
            File.WriteAllText(outsideFile, "outside content");

            WorkspaceManager.TrySwitch(workspace).Success.Should().BeTrue();
            // Process CWD deliberately left elsewhere: the sandbox boundary must
            // come from the workspace, not from Directory.GetCurrentDirectory().
            Directory.GetCurrentDirectory().Should().NotBe(workspace);

            var tool = new ReadFileTool();

            var inside = await tool.ExecuteAsync(new Dictionary<string, object> { { "path", insideFile } });
            inside.Success.Should().BeTrue();
            inside.FormattedOutput.Should().Contain("inside content");

            // A relative path resolves against the workspace.
            var relative = await tool.ExecuteAsync(new Dictionary<string, object> { { "path", "inside.txt" } });
            relative.Success.Should().BeTrue();

            var escape = await tool.ExecuteAsync(new Dictionary<string, object> { { "path", outsideFile } });
            escape.Success.Should().BeFalse();
        }
    }
}
