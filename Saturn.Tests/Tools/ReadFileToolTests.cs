using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using FluentAssertions;
using Saturn.Tools;
using Saturn.Tools.Core;

namespace Saturn.Tests.Tools
{
    public class ReadFileToolTests : IDisposable
    {
        private readonly string _testDirectory;
        private readonly List<string> _createdFiles;

        public ReadFileToolTests()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), $"SaturnTests_{Guid.NewGuid()}");
            Directory.CreateDirectory(_testDirectory);
            _createdFiles = new List<string>();
        }

        [Fact]
        public async Task Execute_WithValidFile_ReturnsFileContent()
        {
            var tool = new ReadFileTool();
            var testFile = CreateTestFile("test.txt", "Hello, World!\nThis is a test file.");
            var parameters = new Dictionary<string, object>
            {
                { "path", testFile }
            };

            var result = await tool.ExecuteAsync(parameters);

            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.FormattedOutput.Should().Contain("Hello, World!");
            result.FormattedOutput.Should().Contain("This is a test file.");
        }

        [Fact]
        public async Task Execute_WithNonExistentFile_ReturnsError()
        {
            var tool = new ReadFileTool();
            var nonExistentFile = Path.Combine(_testDirectory, "nonexistent.txt");
            var parameters = new Dictionary<string, object>
            {
                { "path", nonExistentFile }
            };

            var result = await tool.ExecuteAsync(parameters);

            result.Should().NotBeNull();
            result.Success.Should().BeFalse();
            result.Error.Should().Contain("NOT found");
        }

        [Fact]
        public async Task Execute_WithEmptyFile_ReturnsEmptyContent()
        {
            var tool = new ReadFileTool();
            var testFile = CreateTestFile("empty.txt", "");
            var parameters = new Dictionary<string, object>
            {
                { "path", testFile }
            };

            var result = await tool.ExecuteAsync(parameters);

            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.FormattedOutput.Should().NotBeNullOrEmpty();
            result.FormattedOutput.Should().Contain("Lines: 0");
        }

        [Fact]
        public async Task Execute_WithLargeFile_ReturnsContent()
        {
            var tool = new ReadFileTool();
            var content = new StringBuilder();
            for (int i = 0; i < 100; i++)
            {
                content.AppendLine($"Line {i + 1}: This is test content for line number {i + 1}");
            }
            var testFile = CreateTestFile("large.txt", content.ToString());
            var parameters = new Dictionary<string, object>
            {
                { "path", testFile }
            };

            var result = await tool.ExecuteAsync(parameters);

            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.FormattedOutput.Should().Contain("Line 1:");
            result.FormattedOutput.Should().Contain("Line 100:");
        }

        [Fact]
        public async Task Execute_WithSpecialCharacters_HandlesCorrectly()
        {
            var tool = new ReadFileTool();
            var content = "Special chars: © ® ™ € £ ¥ • … 中文 日本語 한글";
            var testFile = CreateTestFile("special.txt", content);
            var parameters = new Dictionary<string, object>
            {
                { "path", testFile }
            };

            var result = await tool.ExecuteAsync(parameters);

            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.FormattedOutput.Should().Contain("©");
            result.FormattedOutput.Should().Contain("中文");
        }

        [Fact]
        public async Task Execute_WithRelativePath_ReturnsError()
        {
            var tool = new ReadFileTool();
            var parameters = new Dictionary<string, object>
            {
                { "path", "relative/path/file.txt" }
            };

            var result = await tool.ExecuteAsync(parameters);
            
            result.Should().NotBeNull();
            result.Success.Should().BeFalse();
            result.Error.Should().Contain("NOT found");
        }

        [Fact]
        public async Task Execute_WithMissingPath_ReturnsError()
        {
            var tool = new ReadFileTool();
            var parameters = new Dictionary<string, object>();

            var result = await tool.ExecuteAsync(parameters);
            
            result.Should().NotBeNull();
            result.Success.Should().BeFalse();
            result.Error.Should().Contain("CANNOT be empty");
        }

        [Fact]
        public void GetDisplaySummary_ReturnsCorrectSummary()
        {
            var tool = new ReadFileTool();
            var parameters = new Dictionary<string, object>
            {
                { "path", "/path/to/file.txt" }
            };

            var summary = tool.GetDisplaySummary(parameters);

            summary.Should().Contain("file.txt");
        }

        [Fact]
        public void ToolMetadata_IsCorrect()
        {
            var tool = new ReadFileTool();

            tool.Name.Should().Be("read_file");
            tool.Description.Should().Contain("read");
        }

        private string CreateTestFile(string fileName, string content)
        {
            var filePath = Path.Combine(_testDirectory, fileName);
            File.WriteAllText(filePath, content, Encoding.UTF8);
            _createdFiles.Add(filePath);
            return filePath;
        }

        public void Dispose()
        {
            foreach (var file in _createdFiles)
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }

            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, true);
            }
        }
    }
}