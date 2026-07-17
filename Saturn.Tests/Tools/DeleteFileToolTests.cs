using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using Saturn.Tests.TestHelpers;
using Saturn.Tools;
using Xunit;

namespace Saturn.Tests.Tools
{
    [Collection("WorkingDirectory")]
    public class DeleteFileToolTests : IDisposable
    {
        private readonly FileTestHelper _fileHelper;
        private readonly string _originalDirectory;

        public DeleteFileToolTests()
        {
            _fileHelper = new FileTestHelper($"DeleteFileToolTests_{Guid.NewGuid():N}");
            _originalDirectory = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(_fileHelper.TestDirectory);
        }

        public void Dispose()
        {
            Directory.SetCurrentDirectory(_originalDirectory);
            _fileHelper.Dispose();
        }

        [Fact]
        public async System.Threading.Tasks.Task RecursiveWithPattern_DeletesOnlyMatchingFiles_LeavesDirectoriesIntact()
        {
            _fileHelper.CreateFile(Path.Combine("logs", "a.log"), "a");
            _fileHelper.CreateFile(Path.Combine("logs", "b.log"), "b");
            _fileHelper.CreateFile(Path.Combine("logs", "keep.txt"), "keep");
            _fileHelper.CreateFile(Path.Combine("logs", "nested", "c.log"), "c");
            _fileHelper.CreateFile(Path.Combine("logs", "nested", "keep2.txt"), "keep2");

            var tool = new DeleteFileTool();

            var result = await tool.ExecuteAsync(new Dictionary<string, object>
            {
                ["path"] = "logs",
                ["pattern"] = "*.log",
                ["recursive"] = true
            });

            result.Success.Should().BeTrue();

            File.Exists(_fileHelper.GetPath(Path.Combine("logs", "a.log"))).Should().BeFalse();
            File.Exists(_fileHelper.GetPath(Path.Combine("logs", "b.log"))).Should().BeFalse();
            File.Exists(_fileHelper.GetPath(Path.Combine("logs", "nested", "c.log"))).Should().BeFalse();

            File.Exists(_fileHelper.GetPath(Path.Combine("logs", "keep.txt"))).Should().BeTrue();
            File.Exists(_fileHelper.GetPath(Path.Combine("logs", "nested", "keep2.txt"))).Should().BeTrue();
            Directory.Exists(_fileHelper.GetPath("logs")).Should().BeTrue();
            Directory.Exists(_fileHelper.GetPath(Path.Combine("logs", "nested"))).Should().BeTrue();
        }
    }
}
