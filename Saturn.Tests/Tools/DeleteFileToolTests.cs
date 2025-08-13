using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Saturn.Tests.TestHelpers;
using Saturn.Tools;
using Saturn.Tools.Core;
using Xunit;

namespace Saturn.Tests.Tools
{
    public class DeleteFileToolTests : IDisposable
    {
        private readonly DeleteFileTool _tool;
        private readonly FileTestHelper _fileHelper;

        public DeleteFileToolTests()
        {
            _tool = new DeleteFileTool();
            _fileHelper = new FileTestHelper("DeleteFileToolTests");
        }

        public void Dispose()
        {
            _fileHelper?.Dispose();
        }

        [Fact]
        public void DeleteFileTool_Should_Have_Correct_Name()
        {
            _tool.Name.Should().Be("delete_file");
        }

        [Fact]
        public void DeleteFileTool_Should_Have_Description()
        {
            _tool.Description.Should().NotBeNullOrWhiteSpace();
            _tool.Description.Should().Contain("delete");
        }

        [Fact]
        public void DeleteFileTool_Should_Have_Required_Parameters()
        {
            var parameters = _tool.GetParameters();
            parameters.Should().ContainKey("required");
            
            var required = parameters["required"] as string[];
            required.Should().NotBeNull();
            required.Should().Contain("path");
        }

        [Fact]
        public async Task DeleteFileTool_Should_Delete_Single_File()
        {
            // Arrange
            var testFile = _fileHelper.CreateFile("test.txt", "Test content");
            var parameters = new Dictionary<string, object>
            {
                { "path", testFile }
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters);

            // Assert
            result.Success.Should().BeTrue();
            result.FormattedOutput.Should().Contain("Deleted 1 files and 0 directories");
            File.Exists(testFile).Should().BeFalse();
        }

        [Fact]
        public async Task DeleteFileTool_Should_Delete_Empty_Directory()
        {
            // Arrange
            var testDir = _fileHelper.CreateDirectory("empty-dir");
            var parameters = new Dictionary<string, object>
            {
                { "path", testDir }
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters);

            // Assert
            result.Success.Should().BeTrue();
            result.FormattedOutput.Should().Contain("Deleted 0 files and 1 directories");
            Directory.Exists(testDir).Should().BeFalse();
        }

        [Fact]
        public async Task DeleteFileTool_Should_Fail_On_NonEmpty_Directory_Without_Recursive()
        {
            // Arrange
            var testDir = _fileHelper.CreateDirectory("non-empty-dir");
            _fileHelper.CreateFile("non-empty-dir/file.txt", "content");
            var parameters = new Dictionary<string, object>
            {
                { "path", testDir },
                { "recursive", false }
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters);

            // Assert
            result.Success.Should().BeFalse();
            result.Error.Should().Contain("Directory is not empty");
            Directory.Exists(testDir).Should().BeTrue();
        }

        [Fact]
        public async Task DeleteFileTool_Should_Delete_Directory_Recursively()
        {
            // Arrange
            var testDir = _fileHelper.CreateDirectory("recursive-dir");
            _fileHelper.CreateFile("recursive-dir/file1.txt", "content1");
            _fileHelper.CreateFile("recursive-dir/subdir/file2.txt", "content2");
            _fileHelper.CreateDirectory("recursive-dir/empty-subdir");
            
            var parameters = new Dictionary<string, object>
            {
                { "path", testDir },
                { "recursive", true }
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters);

            // Assert
            result.Success.Should().BeTrue();
            result.FormattedOutput.Should().Contain("Deleted 2 files and 3 directories");
            Directory.Exists(testDir).Should().BeFalse();
        }

        [Fact]
        public async Task DeleteFileTool_Should_Delete_Files_By_Pattern()
        {
            // Arrange
            var testDir = _fileHelper.CreateDirectory("pattern-dir");
            var txtFile1 = _fileHelper.CreateFile("pattern-dir/file1.txt", "content1");
            var txtFile2 = _fileHelper.CreateFile("pattern-dir/file2.txt", "content2");
            var logFile = _fileHelper.CreateFile("pattern-dir/file.log", "log content");
            
            var parameters = new Dictionary<string, object>
            {
                { "path", testDir },
                { "pattern", "*.txt" }
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters);

            // Assert
            result.Success.Should().BeTrue();
            result.FormattedOutput.Should().Contain("Deleted 2 files and 0 directories");
            File.Exists(txtFile1).Should().BeFalse();
            File.Exists(txtFile2).Should().BeFalse();
            File.Exists(logFile).Should().BeTrue();
            Directory.Exists(testDir).Should().BeTrue();
        }

        [Fact]
        public async Task DeleteFileTool_Should_Delete_ReadOnly_Files_With_Force()
        {
            // Arrange
            var testFile = _fileHelper.CreateFile("readonly.txt", "Read-only content");
            File.SetAttributes(testFile, FileAttributes.ReadOnly);
            
            var parameters = new Dictionary<string, object>
            {
                { "path", testFile },
                { "force", true }
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters);

            // Assert
            result.Success.Should().BeTrue();
            result.FormattedOutput.Should().Contain("Deleted 1 files");
            File.Exists(testFile).Should().BeFalse();
        }

        [Fact]
        public async Task DeleteFileTool_Should_Fail_On_ReadOnly_Files_Without_Force()
        {
            // Arrange
            var testFile = _fileHelper.CreateFile("readonly-no-force.txt", "Read-only content");
            File.SetAttributes(testFile, FileAttributes.ReadOnly);
            
            var parameters = new Dictionary<string, object>
            {
                { "path", testFile },
                { "force", false }
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters);

            // Assert
            result.Success.Should().BeFalse();
            result.Error.Should().Contain("Failed to delete");
            File.Exists(testFile).Should().BeTrue();
            
            // Cleanup
            File.SetAttributes(testFile, FileAttributes.Normal);
        }

        [Fact]
        public async Task DeleteFileTool_Should_Perform_DryRun_Without_Deleting()
        {
            // Arrange
            var testDir = _fileHelper.CreateDirectory("dryrun-dir");
            var file1 = _fileHelper.CreateFile("dryrun-dir/file1.txt", "content1");
            var file2 = _fileHelper.CreateFile("dryrun-dir/file2.txt", "content2");
            
            var parameters = new Dictionary<string, object>
            {
                { "path", testDir },
                { "recursive", true },
                { "dryRun", true }
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters);

            // Assert
            result.Success.Should().BeTrue();
            result.FormattedOutput.Should().Contain("[DRY RUN]");
            result.FormattedOutput.Should().Contain("Would delete 2 files and 1 directories");
            File.Exists(file1).Should().BeTrue();
            File.Exists(file2).Should().BeTrue();
            Directory.Exists(testDir).Should().BeTrue();
        }

        [Fact]
        public async Task DeleteFileTool_Should_Show_Detailed_DryRun_For_Small_Deletions()
        {
            // Arrange
            var testDir = _fileHelper.CreateDirectory("dryrun-detail-dir");
            var file1 = _fileHelper.CreateFile("dryrun-detail-dir/file1.txt", "content1");
            var file2 = _fileHelper.CreateFile("dryrun-detail-dir/file2.txt", "content2");
            
            var parameters = new Dictionary<string, object>
            {
                { "path", testDir },
                { "recursive", true },
                { "dryRun", true }
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters);

            // Assert
            result.Success.Should().BeTrue();
            result.FormattedOutput.Should().Contain("[F]");
            result.FormattedOutput.Should().Contain("[D]");
            result.FormattedOutput.Should().Contain("file1.txt");
            result.FormattedOutput.Should().Contain("file2.txt");
        }

        [Fact]
        public async Task DeleteFileTool_Should_Truncate_DryRun_For_Large_Deletions()
        {
            // Arrange
            var testDir = _fileHelper.CreateDirectory("dryrun-large-dir");
            for (int i = 0; i < 25; i++)
            {
                _fileHelper.CreateFile($"dryrun-large-dir/file{i}.txt", $"content{i}");
            }
            
            var parameters = new Dictionary<string, object>
            {
                { "path", testDir },
                { "recursive", true },
                { "dryRun", true }
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters);

            // Assert
            result.Success.Should().BeTrue();
            result.FormattedOutput.Should().Contain("First 20 items:");
            result.FormattedOutput.Should().Contain("and 6 more items");
        }

        [Fact]
        public async Task DeleteFileTool_Should_Fail_With_Empty_Path()
        {
            // Arrange
            var parameters = new Dictionary<string, object>
            {
                { "path", "" }
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters);

            // Assert
            result.Success.Should().BeFalse();
            result.Error.Should().Contain("Path cannot be empty");
        }

        [Fact]
        public async Task DeleteFileTool_Should_Fail_With_NonExistent_Path()
        {
            // Arrange
            var nonExistentPath = Path.Combine(_fileHelper.TestDirectory, "non-existent.txt");
            var parameters = new Dictionary<string, object>
            {
                { "path", nonExistentPath }
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters);

            // Assert
            result.Success.Should().BeFalse();
            result.Error.Should().Contain("Path not found");
        }

        [Fact]
        public async Task DeleteFileTool_Should_Calculate_File_Sizes_Correctly()
        {
            // Arrange
            var testFile = _fileHelper.CreateFile("size-test.txt", new string('a', 1024));
            var parameters = new Dictionary<string, object>
            {
                { "path", testFile }
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters);

            // Assert
            result.Success.Should().BeTrue();
            result.FormattedOutput.Should().Contain("1 KB");
            result.RawData.Should().NotBeNull();
        }

        [Fact]
        public async Task DeleteFileTool_Should_Delete_Files_By_Pattern_Recursively_But_Fail_On_NonEmpty_Dirs()
        {
            // Arrange
            var testDir = _fileHelper.CreateDirectory("recursive-pattern-dir");
            var txt1 = _fileHelper.CreateFile("recursive-pattern-dir/file1.txt", "content1");
            var log1 = _fileHelper.CreateFile("recursive-pattern-dir/file1.log", "log1");
            var txt2 = _fileHelper.CreateFile("recursive-pattern-dir/subdir/file2.txt", "content2");
            var log2 = _fileHelper.CreateFile("recursive-pattern-dir/subdir/file2.log", "log2");
            
            var parameters = new Dictionary<string, object>
            {
                { "path", testDir },
                { "pattern", "*.txt" },
                { "recursive", true }
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters);

            // Assert
            result.Success.Should().BeFalse();
            result.Error.Should().Contain("directory is not empty");
            
            File.Exists(txt1).Should().BeFalse();
            File.Exists(txt2).Should().BeFalse();
            File.Exists(log1).Should().BeTrue();
            File.Exists(log2).Should().BeTrue();
            Directory.Exists(testDir).Should().BeTrue();
        }
        
        [Fact]
        public async Task DeleteFileTool_Should_Delete_All_Files_Recursively_With_Wildcard_Pattern()
        {
            // Arrange
            var testDir = _fileHelper.CreateDirectory("recursive-wildcard-dir");
            var txt1 = _fileHelper.CreateFile("recursive-wildcard-dir/file1.txt", "content1");
            var log1 = _fileHelper.CreateFile("recursive-wildcard-dir/file1.log", "log1");
            var txt2 = _fileHelper.CreateFile("recursive-wildcard-dir/subdir/file2.txt", "content2");
            var log2 = _fileHelper.CreateFile("recursive-wildcard-dir/subdir/file2.log", "log2");
            
            var parameters = new Dictionary<string, object>
            {
                { "path", testDir },
                { "pattern", "*" },  // Match all files
                { "recursive", true }
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters);

            // Assert
            result.Success.Should().BeTrue();
            // When all files are deleted, directories can be deleted too
            result.FormattedOutput.Should().Contain("Deleted 4 files and 2 directories");
            File.Exists(txt1).Should().BeFalse();
            File.Exists(txt2).Should().BeFalse();
            File.Exists(log1).Should().BeFalse();
            File.Exists(log2).Should().BeFalse();
            Directory.Exists(testDir).Should().BeFalse();
        }

        [Fact]
        public void DeleteFileTool_GetDisplaySummary_Should_Return_Formatted_Summary()
        {
            // Arrange
            var parameters = new Dictionary<string, object>
            {
                { "path", "/some/path/to/file.txt" }
            };

            // Act
            var summary = _tool.GetDisplaySummary(parameters);

            // Assert
            summary.Should().Be("Deleting file.txt");
        }

        [Fact]
        public void DeleteFileTool_GetDisplaySummary_Should_Handle_Empty_Path()
        {
            // Arrange
            var parameters = new Dictionary<string, object>
            {
                { "path", "" }
            };

            // Act
            var summary = _tool.GetDisplaySummary(parameters);

            // Assert
            summary.Should().Be("Deleting unknown");
        }

        [Fact]
        public async Task DeleteFileTool_Should_Return_Success_When_Nothing_To_Delete()
        {
            // Arrange
            var testDir = _fileHelper.CreateDirectory("empty-pattern-dir");
            var parameters = new Dictionary<string, object>
            {
                { "path", testDir },
                { "pattern", "*.nonexistent" }
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters);

            // Assert
            result.Success.Should().BeTrue();
            result.FormattedOutput.Should().Be("Nothing to delete");
            Directory.Exists(testDir).Should().BeTrue();
        }

        [Fact]
        public async Task DeleteFileTool_Should_Order_Directory_Deletion_Correctly()
        {
            // Arrange
            var testDir = _fileHelper.CreateDirectory("nested-dir");
            _fileHelper.CreateDirectory("nested-dir/level1");
            _fileHelper.CreateDirectory("nested-dir/level1/level2");
            _fileHelper.CreateDirectory("nested-dir/level1/level2/level3");
            _fileHelper.CreateFile("nested-dir/level1/level2/level3/deep.txt", "deep content");
            
            var parameters = new Dictionary<string, object>
            {
                { "path", testDir },
                { "recursive", true }
            };

            // Act
            var result = await _tool.ExecuteAsync(parameters);

            // Assert
            result.Success.Should().BeTrue();
            result.FormattedOutput.Should().Contain("Deleted 1 files and 4 directories");
            Directory.Exists(testDir).Should().BeFalse();
        }
    }
}