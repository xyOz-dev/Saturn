using System;
using System.IO;
using FluentAssertions;
using Saturn.Core.Workspace;
using Saturn.Tests.TestHelpers;
using Xunit;

namespace Saturn.Tests.Core
{
    [Collection("WorkingDirectory")]
    public class WorkspaceManagerTests : IDisposable
    {
        private readonly FileTestHelper _files = new();

        public void Dispose()
        {
            WorkspaceManager.ResetForTests();
            _files.Dispose();
        }

        [Fact]
        public void CurrentWorkspace_Uninitialized_TracksProcessWorkingDirectory()
        {
            WorkspaceManager.ResetForTests();
            WorkspaceManager.CurrentWorkspace.Should().Be(Environment.CurrentDirectory);
        }

        [Fact]
        public void Initialize_NullPath_UsesCurrentDirectory()
        {
            WorkspaceManager.Initialize(null);
            WorkspaceManager.CurrentWorkspace.Should().Be(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(Environment.CurrentDirectory)));
        }

        [Fact]
        public void Initialize_NonexistentPath_Throws()
        {
            var missing = Path.Combine(_files.TestDirectory, "does-not-exist");
            var act = () => WorkspaceManager.Initialize(missing);
            act.Should().Throw<DirectoryNotFoundException>();
        }

        [Fact]
        public void TrySwitch_ValidDirectory_SwitchesAndNormalizes()
        {
            var target = _files.CreateDirectory("workspace-a");

            var result = WorkspaceManager.TrySwitch(target + Path.DirectorySeparatorChar);

            result.Success.Should().BeTrue();
            result.NormalizedPath.Should().Be(target);
            WorkspaceManager.CurrentWorkspace.Should().Be(target);
            WorkspaceManager.WorkspaceName.Should().Be("workspace-a");
        }

        [Fact]
        public void TrySwitch_RelativePath_ResolvesAgainstCurrentWorkspace()
        {
            var root = _files.CreateDirectory("root");
            var child = _files.CreateDirectory(Path.Combine("root", "child"));
            WorkspaceManager.TrySwitch(root).Success.Should().BeTrue();

            var result = WorkspaceManager.TrySwitch("child");

            result.Success.Should().BeTrue();
            result.NormalizedPath.Should().Be(child);
        }

        [Fact]
        public void TrySwitch_NonexistentDirectory_Fails()
        {
            var missing = Path.Combine(_files.TestDirectory, "missing");
            var result = WorkspaceManager.TrySwitch(missing);

            result.Success.Should().BeFalse();
            result.Error.Should().Contain("does not exist");
        }

        [Fact]
        public void TrySwitch_FilePath_Fails()
        {
            var file = _files.CreateFile("a-file.txt", "content");
            var result = WorkspaceManager.TrySwitch(file);

            result.Success.Should().BeFalse();
            result.Error.Should().Contain("file");
        }

        [Fact]
        public void TrySwitch_EmptyPath_Fails()
        {
            WorkspaceManager.TrySwitch("  ").Success.Should().BeFalse();
        }

        [Fact]
        public void TrySwitch_SamePath_IsNoOpSuccessAndDoesNotRaiseEvent()
        {
            var target = _files.CreateDirectory("workspace-b");
            WorkspaceManager.TrySwitch(target).Success.Should().BeTrue();

            var raised = false;
            Action<string, string> handler = (_, _) => raised = true;
            WorkspaceManager.WorkspaceChanged += handler;
            try
            {
                var result = WorkspaceManager.TrySwitch(target.ToUpperInvariant());
                result.Success.Should().BeTrue();
                raised.Should().BeFalse();
            }
            finally
            {
                WorkspaceManager.WorkspaceChanged -= handler;
            }
        }

        [Fact]
        public void TrySwitch_RaisesWorkspaceChangedWithOldAndNewPaths()
        {
            var first = _files.CreateDirectory("first");
            var second = _files.CreateDirectory("second");
            WorkspaceManager.TrySwitch(first).Success.Should().BeTrue();

            string? oldPath = null, newPath = null;
            Action<string, string> handler = (o, n) => { oldPath = o; newPath = n; };
            WorkspaceManager.WorkspaceChanged += handler;
            try
            {
                WorkspaceManager.TrySwitch(second).Success.Should().BeTrue();
            }
            finally
            {
                WorkspaceManager.WorkspaceChanged -= handler;
            }

            oldPath.Should().Be(first);
            newPath.Should().Be(second);
        }
    }
}
