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
            // Arrange
            var tool = new ReadFileTool();
            var testFile = CreateTestFile("test.txt", "Hello, World!\nThis is a test file.");
            var parameters = new Dictionary<string, object>
            {
                { "path", testFile }
            };

            // Act
            var result = await tool.ExecuteAsync(parameters);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.FormattedOutput.Should().Contain("Hello, World!");
            result.FormattedOutput.Should().Contain("This is a test file.");
        }

        [Fact]
        public async Task Execute_WithNonExistentFile_ReturnsError()
        {
            // Arrange
            var tool = new ReadFileTool();
            var nonExistentFile = Path.Combine(_testDirectory, "nonexistent.txt");
            var parameters = new Dictionary<string, object>
            {
                { "path", nonExistentFile }
            };

            // Act
            var result = await tool.ExecuteAsync(parameters);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeFalse();
            result.Error.Should().Contain("NOT found");
        }

        [Fact]
        public async Task Execute_WithEmptyFile_ReturnsEmptyContent()
        {
            // Arrange
            var tool = new ReadFileTool();
            var testFile = CreateTestFile("empty.txt", "");
            var parameters = new Dictionary<string, object>
            {
                { "path", testFile }
            };

            // Act
            var result = await tool.ExecuteAsync(parameters);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.FormattedOutput.Should().NotBeNullOrEmpty();
            result.FormattedOutput.Should().Contain("Lines: 0");
        }

        [Fact]
        public async Task Execute_WithLargeFile_ReturnsContent()
        {
            // Arrange
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

            // Act
            var result = await tool.ExecuteAsync(parameters);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.FormattedOutput.Should().Contain("Line 1:");
            result.FormattedOutput.Should().Contain("Line 100:");
        }

        [Fact]
        public async Task Execute_WithSpecialCharacters_HandlesCorrectly()
        {
            // Arrange
            var tool = new ReadFileTool();
            var content = "Special chars: © ® ™ € £ ¥ • … 中文 日本語 한글";
            var testFile = CreateTestFile("special.txt", content);
            var parameters = new Dictionary<string, object>
            {
                { "path", testFile }
            };

            // Act
            var result = await tool.ExecuteAsync(parameters);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.FormattedOutput.Should().Contain("©");
            result.FormattedOutput.Should().Contain("中文");
        }

        [Fact]
        public async Task Execute_WithRelativePath_ReturnsError()
        {
            // Arrange
            var tool = new ReadFileTool();
            var parameters = new Dictionary<string, object>
            {
                { "path", "relative/path/file.txt" }
            };

            // Act
            var result = await tool.ExecuteAsync(parameters);
            
            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeFalse();
            result.Error.Should().Contain("NOT found");
        }

        [Fact]
        public async Task Execute_WithMissingPath_ReturnsError()
        {
            // Arrange
            var tool = new ReadFileTool();
            var parameters = new Dictionary<string, object>();

            // Act
            var result = await tool.ExecuteAsync(parameters);
            
            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeFalse();
            result.Error.Should().Contain("CANNOT be empty");
        }

        [Fact]
        public void GetDisplaySummary_ReturnsCorrectSummary()
        {
            // Arrange
            var tool = new ReadFileTool();
            var parameters = new Dictionary<string, object>
            {
                { "path", "/path/to/file.txt" }
            };

            // Act
            var summary = tool.GetDisplaySummary(parameters);

            // Assert
            summary.Should().Contain("file.txt");
        }

        [Fact]
        public void ToolMetadata_IsCorrect()
        {
            // Arrange
            var tool = new ReadFileTool();

            // Assert
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
            // Clean up test files
            foreach (var file in _createdFiles)
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }

            // Remove test directory
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, true);
            }
        }
    }
}