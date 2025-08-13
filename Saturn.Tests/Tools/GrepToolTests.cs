using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using FluentAssertions;
using Saturn.Tools;
using Saturn.Tools.Core;
using Saturn.Tests.TestHelpers;

namespace Saturn.Tests.Tools
{
    public class GrepToolTests : IDisposable
    {
        private readonly FileTestHelper _fileHelper;
        private readonly GrepTool _tool;

        public GrepToolTests()
        {
            _fileHelper = new FileTestHelper("GrepToolTests");
            _tool = new GrepTool();
        }

        #region Basic Functionality Tests

        [Fact]
        public async Task Execute_WithSimplePattern_FindsMatches()
        {
            // Arrange
            var testFile = _fileHelper.CreateFile("test.txt", 
                "Hello World\nThis is a test\nHello again\nGoodbye");
            
            var parameters = new Dictionary<string, object>
            {
                { "pattern", "Hello" },
                { "path", testFile }
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.FormattedOutput.Should().Contain("Found 2 matches");
            result.FormattedOutput.Should().Contain("Hello World");
            result.FormattedOutput.Should().Contain("Hello again");
            result.FormattedOutput.Should().NotContain("Goodbye");
        }

        [Fact]
        public async Task Execute_WithRegexPattern_FindsComplexMatches()
        {
            // Arrange
            var testFile = _fileHelper.CreateFile("code.cs",
                "public class UserService\n" +
                "{\n" +
                "    private readonly IUserRepository _repository;\n" +
                "    public void SaveUser(User user) { }\n" +
                "    public User GetUser(int id) { return null; }\n" +
                "}");
            
            var parameters = new Dictionary<string, object>
            {
                { "pattern", @"public\s+\w+\s+\w+\(" },
                { "path", testFile }
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.FormattedOutput.Should().Contain("Found 2 matches");
            result.FormattedOutput.Should().Contain("SaveUser");
            result.FormattedOutput.Should().Contain("GetUser");
        }

        [Fact]
        public async Task Execute_WithNoMatches_ReturnsNoMatchesMessage()
        {
            // Arrange
            var testFile = _fileHelper.CreateFile("test.txt", "Hello World");
            
            var parameters = new Dictionary<string, object>
            {
                { "pattern", "NonExistentPattern" },
                { "path", testFile }
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.FormattedOutput.Should().Contain("No matches found");
        }

        #endregion

        #region Directory Search Tests

        [Fact]
        public async Task Execute_InDirectory_SearchesAllFiles()
        {
            // Arrange
            _fileHelper.CreateFile("file1.txt", "Hello World");
            _fileHelper.CreateFile("file2.txt", "Hello Again");
            _fileHelper.CreateFile("file3.txt", "Goodbye");
            
            var parameters = new Dictionary<string, object>
            {
                { "pattern", "Hello" },
                { "path", _fileHelper.TestDirectory }
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.FormattedOutput.Should().Contain("Found 2 matches");
            result.FormattedOutput.Should().Contain("file1.txt");
            result.FormattedOutput.Should().Contain("file2.txt");
            result.FormattedOutput.Should().NotContain("file3.txt");
        }

        [Fact]
        public async Task Execute_WithRecursive_SearchesSubdirectories()
        {
            // Arrange
            _fileHelper.CreateFile("root.txt", "Hello from root");
            _fileHelper.CreateFile("subdir/sub1.txt", "Hello from sub1");
            _fileHelper.CreateFile("subdir/deep/sub2.txt", "Hello from sub2");
            
            var parameters = new Dictionary<string, object>
            {
                { "pattern", "Hello" },
                { "path", _fileHelper.TestDirectory },
                { "recursive", true }
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.FormattedOutput.Should().Contain("Found 3 matches");
            result.FormattedOutput.Should().Contain("root.txt");
            result.FormattedOutput.Should().Contain("sub1.txt");
            result.FormattedOutput.Should().Contain("sub2.txt");
        }

        [Fact]
        public async Task Execute_WithoutRecursive_SkipsSubdirectories()
        {
            // Arrange
            _fileHelper.CreateFile("root.txt", "Hello from root");
            _fileHelper.CreateFile("subdir/sub1.txt", "Hello from sub1");
            
            var parameters = new Dictionary<string, object>
            {
                { "pattern", "Hello" },
                { "path", _fileHelper.TestDirectory },
                { "recursive", false }
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.FormattedOutput.Should().Contain("Found 1 match");
            result.FormattedOutput.Should().Contain("root.txt");
            result.FormattedOutput.Should().NotContain("sub1.txt");
        }

        #endregion

        #region File Pattern Filter Tests

        [Fact]
        public async Task Execute_WithFilePattern_FiltersFiles()
        {
            // Arrange
            _fileHelper.CreateFile("test.cs", "public class Test { }");
            _fileHelper.CreateFile("test.txt", "public class Test { }");
            _fileHelper.CreateFile("test.md", "public class Test { }");
            
            var parameters = new Dictionary<string, object>
            {
                { "pattern", "public" },
                { "path", _fileHelper.TestDirectory },
                { "filePattern", "*.cs" }
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.FormattedOutput.Should().Contain("Found 1 match");
            result.FormattedOutput.Should().Contain("test.cs");
            result.FormattedOutput.Should().NotContain("test.txt");
            result.FormattedOutput.Should().NotContain("test.md");
        }

        [Fact]
        public async Task Execute_WithComplexFilePattern_FiltersCorrectly()
        {
            // Arrange
            _fileHelper.CreateFile("UserService.cs", "class UserService");
            _fileHelper.CreateFile("UserRepository.cs", "class UserRepository");
            _fileHelper.CreateFile("ProductService.cs", "class ProductService");
            
            var parameters = new Dictionary<string, object>
            {
                { "pattern", "class" },
                { "path", _fileHelper.TestDirectory },
                { "filePattern", "User*.cs" }
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.FormattedOutput.Should().Contain("Found 2 matches");
            result.FormattedOutput.Should().Contain("UserService.cs");
            result.FormattedOutput.Should().Contain("UserRepository.cs");
            result.FormattedOutput.Should().NotContain("ProductService.cs");
        }

        #endregion

        #region Case Sensitivity Tests

        [Fact]
        public async Task Execute_WithIgnoreCase_FindsCaseInsensitiveMatches()
        {
            // Arrange
            var testFile = _fileHelper.CreateFile("test.txt", 
                "HELLO WORLD\nhello world\nHeLLo WoRLd");
            
            var parameters = new Dictionary<string, object>
            {
                { "pattern", "hello" },
                { "path", testFile },
                { "ignoreCase", true }
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.FormattedOutput.Should().Contain("Found 3 matches");
        }

        [Fact]
        public async Task Execute_WithoutIgnoreCase_FindsCaseSensitiveMatches()
        {
            // Arrange
            var testFile = _fileHelper.CreateFile("test.txt", 
                "HELLO WORLD\nhello world\nHeLLo WoRLd");
            
            var parameters = new Dictionary<string, object>
            {
                { "pattern", "hello" },
                { "path", testFile },
                { "ignoreCase", false }
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.FormattedOutput.Should().Contain("Found 1 match");
            result.FormattedOutput.Should().Contain("hello world");
        }

        #endregion

        #region Max Results Tests

        [Fact]
        public async Task Execute_WithMaxResults_LimitsOutput()
        {
            // Arrange
            var content = string.Join("\n", Enumerable.Range(1, 100).Select(i => $"Line {i}: Test"));
            var testFile = _fileHelper.CreateFile("test.txt", content);
            
            var parameters = new Dictionary<string, object>
            {
                { "pattern", "Test" },
                { "path", testFile },
                { "maxResults", 5 }
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.FormattedOutput.Should().Contain("Found 5 matches");
            result.FormattedOutput.Should().Contain("Line 1:");
            result.FormattedOutput.Should().Contain("Line 5:");
            result.FormattedOutput.Should().NotContain("Line 6:");
        }

        [Fact]
        public async Task Execute_WithMaxResultsInMultipleFiles_LimitsAcrossFiles()
        {
            // Arrange
            // Create files with different numbers of matches to test the limit
            _fileHelper.CreateFile("file1.txt", "Match line 1\nMatch line 2");
            _fileHelper.CreateFile("file2.txt", "Match line 3\nMatch line 4");
            _fileHelper.CreateFile("file3.txt", "Match line 5\nMatch line 6");
            _fileHelper.CreateFile("file4.txt", "Match line 7\nMatch line 8");
            
            var parameters = new Dictionary<string, object>
            {
                { "pattern", "Match" },
                { "path", _fileHelper.TestDirectory },
                { "maxResults", 5 }
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            
            // After the fix, it should correctly limit to exactly 5 matches across files
            var grepResults = result.RawData as List<GrepTool.GrepResult>;
            grepResults.Should().NotBeNull();
            grepResults.Should().HaveCount(5);
            result.FormattedOutput.Should().Contain("Found 5 matches");
            
            // Verify the matches are from multiple files (first 3 files should have matches)
            var filesWithMatches = grepResults.Select(r => Path.GetFileName(r.FilePath)).Distinct().ToList();
            filesWithMatches.Should().HaveCountGreaterThan(1); // Should span multiple files
        }

        [Fact]
        public async Task Execute_WithMaxResults_StopsExactlyAtLimit()
        {
            // Arrange - Create a scenario that would have failed with the bug
            // The bug was that SearchFile checked results.Count < maxResults globally
            // instead of checking against the remainingResults parameter
            _fileHelper.CreateFile("a.txt", "Match 1");
            _fileHelper.CreateFile("b.txt", "Match 2");
            _fileHelper.CreateFile("c.txt", "Match 3");
            _fileHelper.CreateFile("d.txt", "Match 4");
            _fileHelper.CreateFile("e.txt", "Match 5");
            _fileHelper.CreateFile("f.txt", "Match 6");
            _fileHelper.CreateFile("g.txt", "Match 7");
            
            var parameters = new Dictionary<string, object>
            {
                { "pattern", "Match" },
                { "path", _fileHelper.TestDirectory },
                { "maxResults", 5 }
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            
            var grepResults = result.RawData as List<GrepTool.GrepResult>;
            grepResults.Should().NotBeNull();
            grepResults.Should().HaveCount(5, "should stop exactly at maxResults");
            
            // Verify we got matches from exactly 5 files (a.txt through e.txt)
            var matchedFiles = grepResults.Select(r => Path.GetFileName(r.FilePath)).ToList();
            matchedFiles.Should().BeEquivalentTo(new[] { "a.txt", "b.txt", "c.txt", "d.txt", "e.txt" });
            
            // Ensure file f.txt and g.txt were not searched
            result.FormattedOutput.Should().NotContain("f.txt");
            result.FormattedOutput.Should().NotContain("g.txt");
        }

        #endregion

        #region Error Handling Tests

        [Fact]
        public async Task Execute_WithNonExistentPath_ReturnsError()
        {
            // Arrange
            var parameters = new Dictionary<string, object>
            {
                { "pattern", "test" },
                { "path", Path.Combine(_fileHelper.TestDirectory, "nonexistent.txt") }
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeFalse();
            result.Error.Should().Contain("NOT found");
        }

        [Fact]
        public async Task Execute_WithEmptyPattern_ReturnsError()
        {
            // Arrange
            var testFile = _fileHelper.CreateFile("test.txt", "Hello World");
            var parameters = new Dictionary<string, object>
            {
                { "pattern", "" },
                { "path", testFile }
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeFalse();
            result.Error.Should().Contain("Pattern CANNOT be empty");
        }

        [Fact]
        public async Task Execute_WithEmptyPath_ReturnsError()
        {
            // Arrange
            var parameters = new Dictionary<string, object>
            {
                { "pattern", "test" },
                { "path", "" }
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeFalse();
            result.Error.Should().Contain("Path CANNOT be empty");
        }

        [Fact]
        public async Task Execute_WithInvalidRegex_HandlesGracefully()
        {
            // Arrange
            var testFile = _fileHelper.CreateFile("test.txt", "Hello World");
            var parameters = new Dictionary<string, object>
            {
                { "pattern", "[" }, // Invalid regex
                { "path", testFile }
            };

            // Act & Assert
            await Assert.ThrowsAsync<System.Text.RegularExpressions.RegexParseException>(
                async () => await _tool.ExecuteAsync(parameters));
        }

        #endregion

        #region Special Content Tests

        [Fact]
        public async Task Execute_WithMultipleMatchesPerLine_ReportsAllMatches()
        {
            // Arrange
            var testFile = _fileHelper.CreateFile("test.txt", 
                "Hello Hello Hello\nHello World Hello");
            
            var parameters = new Dictionary<string, object>
            {
                { "pattern", "Hello" },
                { "path", testFile }
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            
            var grepResults = result.RawData as List<GrepTool.GrepResult>;
            grepResults.Should().NotBeNull();
            grepResults.Should().HaveCount(2);
            grepResults[0].Matches.Should().HaveCount(3);
            grepResults[1].Matches.Should().HaveCount(2);
        }

        [Fact]
        public async Task Execute_WithSpecialCharacters_HandlesCorrectly()
        {
            // Arrange
            var testFile = _fileHelper.CreateFile("test.txt", 
                "Special: © ® ™\nUnicode: 中文 日本語 한글\nEmoji: 😀 🎉 🚀");
            
            var parameters = new Dictionary<string, object>
            {
                { "pattern", "中文|日本語" },
                { "path", testFile }
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.FormattedOutput.Should().Contain("Found 1 match");
            result.FormattedOutput.Should().Contain("中文");
            result.FormattedOutput.Should().Contain("日本語");
        }

        [Fact]
        public async Task Execute_WithEmptyFile_ReturnsNoMatches()
        {
            // Arrange
            var testFile = _fileHelper.CreateFile("empty.txt", "");
            
            var parameters = new Dictionary<string, object>
            {
                { "pattern", "test" },
                { "path", testFile }
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.FormattedOutput.Should().Contain("No matches found");
        }

        [Fact]
        public async Task Execute_WithBinaryFile_HandlesGracefully()
        {
            // Arrange
            var binaryContent = new byte[] { 0x00, 0x01, 0x02, 0xFF, 0xFE, 0xFD };
            var testFile = _fileHelper.CreateBinaryFile("binary.bin", binaryContent);
            
            var parameters = new Dictionary<string, object>
            {
                { "pattern", "test" },
                { "path", testFile }
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.FormattedOutput.Should().Contain("No matches found");
        }

        #endregion

        #region Line Number Tests

        [Fact]
        public async Task Execute_ReportsCorrectLineNumbers()
        {
            // Arrange
            var testFile = _fileHelper.CreateFile("test.txt", 
                "Line 1\nLine 2\nMatch on line 3\nLine 4\nMatch on line 5");
            
            var parameters = new Dictionary<string, object>
            {
                { "pattern", "Match" },
                { "path", testFile }
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.FormattedOutput.Should().Contain(":3");
            result.FormattedOutput.Should().Contain(":5");
            result.FormattedOutput.Should().Contain("Match on line 3");
            result.FormattedOutput.Should().Contain("Match on line 5");
        }

        #endregion

        #region Tool Metadata Tests

        [Fact]
        public void Name_ReturnsCorrectValue()
        {
            // Assert
            _tool.Name.Should().Be("grep");
        }

        [Fact]
        public void Description_ContainsUsefulInformation()
        {
            // Assert
            _tool.Description.Should().Contain("search");
            _tool.Description.Should().Contain("pattern");
            _tool.Description.Should().Contain("files");
        }

        [Fact]
        public void GetParameters_ReturnsCorrectSchema()
        {
            // Act
            var parameters = _tool.GetParameters();

            // Assert
            parameters.Should().ContainKey("type");
            parameters["type"].Should().Be("object");
            parameters.Should().ContainKey("properties");
            parameters.Should().ContainKey("required");
            
            var required = parameters["required"] as string[];
            required.Should().NotBeNull();
            required.Should().Contain("pattern");
            required.Should().Contain("path");
        }

        [Fact]
        public void GetDisplaySummary_ReturnsCorrectSummary()
        {
            // Arrange
            var parameters = new Dictionary<string, object>
            {
                { "pattern", "test pattern that is very long and should be truncated" },
                { "path", "/very/long/path/that/should/also/be/truncated/file.txt" }
            };

            // Act
            var summary = _tool.GetDisplaySummary(parameters);

            // Assert
            summary.Should().Contain("Searching for");
            summary.Should().Contain("...");
            summary.Should().NotContain("very long and should be truncated");
        }

        #endregion

        #region Performance Tests

        [Fact]
        public async Task Execute_WithLargeFile_PerformsReasonably()
        {
            // Arrange
            var lines = Enumerable.Range(1, 10000).Select(i => $"Line {i}: Some test content with pattern on line {i}");
            var content = string.Join("\n", lines);
            var testFile = _fileHelper.CreateFile("large.txt", content);
            
            var parameters = new Dictionary<string, object>
            {
                { "pattern", "pattern" },
                { "path", testFile },
                { "maxResults", 100 }
            };

            // Act
            var startTime = DateTime.Now;
            var result = await _tool.ExecuteAsync(parameters);
            var duration = DateTime.Now - startTime;

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.FormattedOutput.Should().Contain("Found 100 matches");
            duration.TotalSeconds.Should().BeLessThan(5); // Should complete within 5 seconds
        }

        #endregion

        public void Dispose()
        {
            _fileHelper?.Dispose();
        }
    }
}