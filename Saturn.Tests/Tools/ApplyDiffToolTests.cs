using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Saturn.Tests.TestHelpers;
using Saturn.Tools;
using Saturn.Tools.Core;
using Xunit;

namespace Saturn.Tests.Tools
{
    public class ApplyDiffToolTests : IDisposable
    {
        private readonly FileTestHelper _fileHelper;
        private readonly ApplyDiffTool _tool;
        private readonly string _originalDirectory;

        public ApplyDiffToolTests()
        {
            _fileHelper = new FileTestHelper("ApplyDiffToolTests");
            _tool = new ApplyDiffTool();
            _originalDirectory = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(_fileHelper.TestDirectory);
        }

        public void Dispose()
        {
            Directory.SetCurrentDirectory(_originalDirectory);
            _fileHelper?.Dispose();
        }

        #region Basic File Operations

        [Fact]
        public async Task ExecuteAsync_AddNewFile_CreatesFileWithCorrectContent()
        {
            var patchText = @"*** Add File: test.txt
+Line 1
+Line 2
+Line 3";

            var parameters = new Dictionary<string, object>
            {
                { "patchText", patchText }
            };

            var result = await _tool.ExecuteAsync(parameters);

            result.Success.Should().BeTrue();
            _fileHelper.FileExists("test.txt").Should().BeTrue();
            
            // Platform-aware line ending check
            var expectedContent = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? "Line 1\r\nLine 2\r\nLine 3"
                : "Line 1\nLine 2\nLine 3";
            
            _fileHelper.ReadFile("test.txt").Should().Be(expectedContent);
        }

        [Fact]
        public async Task ExecuteAsync_DeleteFile_RemovesExistingFile()
        {
            _fileHelper.CreateFile("delete-me.txt", "Content to delete");

            var patchText = "*** Delete File: delete-me.txt";
            var parameters = new Dictionary<string, object>
            {
                { "patchText", patchText }
            };

            var result = await _tool.ExecuteAsync(parameters);

            result.Success.Should().BeTrue();
            _fileHelper.FileExists("delete-me.txt").Should().BeFalse();
        }

        [Fact]
        public async Task ExecuteAsync_UpdateFile_ModifiesExistingContent()
        {
            _fileHelper.CreateFile("update.txt", @"public class Test
{
    var old = true;
}");

            var patchText = @"*** Update File: update.txt
@@ var old = true; @@
-    var old = true;
+    var updated = false;
+    var newLine = GetValue();";

            var parameters = new Dictionary<string, object>
            {
                { "patchText", patchText }
            };

            var result = await _tool.ExecuteAsync(parameters);

            result.Success.Should().BeTrue();
            var content = _fileHelper.ReadFile("update.txt");
            content.Should().Contain("var updated = false");
            content.Should().Contain("var newLine = GetValue()");
            content.Should().NotContain("var old = true");
        }

        #endregion

        #region Multiple Operations

        [Fact]
        public async Task ExecuteAsync_MultipleOperations_AppliesAllChanges()
        {
            _fileHelper.CreateFile("existing.txt", "Original content");
            _fileHelper.CreateFile("to-delete.txt", "Will be deleted");

            var patchText = @"*** Add File: new.txt
+New file content
*** Update File: existing.txt
@@ Original content @@
-Original content
+Updated content
*** Delete File: to-delete.txt";

            var parameters = new Dictionary<string, object>
            {
                { "patchText", patchText }
            };

            var result = await _tool.ExecuteAsync(parameters);

            result.Success.Should().BeTrue();
            _fileHelper.FileExists("new.txt").Should().BeTrue();
            _fileHelper.ReadFile("new.txt").Should().Be("New file content");
            _fileHelper.ReadFile("existing.txt").Should().Be("Updated content");
            _fileHelper.FileExists("to-delete.txt").Should().BeFalse();
        }

        #endregion

        #region Context Matching

        [Fact]
        public async Task ExecuteAsync_ContextLineNotFound_ReturnsError()
        {
            _fileHelper.CreateFile("test.txt", "Line 1\nLine 2\nLine 3");

            var patchText = @"*** Update File: test.txt
@@ Non-existent context @@
-Line 2
+Modified Line 2";

            var parameters = new Dictionary<string, object>
            {
                { "patchText", patchText }
            };

            var result = await _tool.ExecuteAsync(parameters);

            result.Success.Should().BeFalse();
            result.Error.Should().Contain("Context line not found");
        }

        [Fact]
        public async Task ExecuteAsync_MultipleHunks_AppliesInOrder()
        {
            _fileHelper.CreateFile("multi-hunk.txt", @"Section 1
Content A
Section 2
Content B
Section 3
Content C");

            var patchText = @"*** Update File: multi-hunk.txt
@@ Content A @@
+Added after Content A
@@ Content B @@
+Added after Content B
@@ Content C @@
-Content C
+Modified Content C";

            var parameters = new Dictionary<string, object>
            {
                { "patchText", patchText }
            };

            var result = await _tool.ExecuteAsync(parameters);

            result.Success.Should().BeTrue();
            var content = _fileHelper.ReadFile("multi-hunk.txt");
            content.Should().Contain("Added after Content A");
            content.Should().Contain("Added after Content B");
            content.Should().Contain("Modified Content C");
            content.Should().NotContain("Content C\r\nModified");
        }

        #endregion

        #region Dry Run

        [Fact]
        public async Task ExecuteAsync_DryRun_DoesNotModifyFiles()
        {
            var originalContent = "Original content";
            _fileHelper.CreateFile("dryrun.txt", originalContent);

            var patchText = @"*** Update File: dryrun.txt
@@ Original content @@
-Original content
+Modified content";

            var parameters = new Dictionary<string, object>
            {
                { "patchText", patchText },
                { "dryRun", true }
            };

            var result = await _tool.ExecuteAsync(parameters);

            result.Success.Should().BeTrue();
            result.FormattedOutput.Should().Contain("[DRY RUN]");
            _fileHelper.ReadFile("dryrun.txt").Should().Be(originalContent);
        }

        [Fact]
        public async Task ExecuteAsync_DryRunWithInvalidPatch_ReturnsError()
        {
            var patchText = @"*** Update File: non-existent.txt
@@ Some context @@
-Remove this";

            var parameters = new Dictionary<string, object>
            {
                { "patchText", patchText },
                { "dryRun", true }
            };

            var result = await _tool.ExecuteAsync(parameters);

            result.Success.Should().BeFalse();
            result.Error.Should().Contain("File not found");
        }

        #endregion

        #region Error Handling

        [Fact]
        public async Task ExecuteAsync_EmptyPatchText_ReturnsError()
        {
            var parameters = new Dictionary<string, object>
            {
                { "patchText", "" }
            };

            var result = await _tool.ExecuteAsync(parameters);

            result.Success.Should().BeFalse();
            result.Error.Should().Contain("Patch text cannot be empty");
        }

        [Fact]
        public async Task ExecuteAsync_MissingRequiredParameter_ReturnsError()
        {
            var parameters = new Dictionary<string, object>();

            var result = await _tool.ExecuteAsync(parameters);

            result.Success.Should().BeFalse();
        }

        [Fact]
        public async Task ExecuteAsync_AddExistingFile_ReturnsError()
        {
            _fileHelper.CreateFile("existing.txt", "Already here");

            var patchText = @"*** Add File: existing.txt
+New content";

            var parameters = new Dictionary<string, object>
            {
                { "patchText", patchText }
            };

            var result = await _tool.ExecuteAsync(parameters);

            result.Success.Should().BeFalse();
            result.Error.Should().Contain("already exists");
        }

        [Fact]
        public async Task ExecuteAsync_UpdateNonExistentFile_ReturnsError()
        {
            var patchText = @"*** Update File: missing.txt
@@ Some context @@
-Remove this";

            var parameters = new Dictionary<string, object>
            {
                { "patchText", patchText }
            };

            var result = await _tool.ExecuteAsync(parameters);

            result.Success.Should().BeFalse();
            result.Error.Should().Contain("File not found");
        }

        [Fact]
        public async Task ExecuteAsync_PathTraversal_ReturnsSecurityError()
        {
            var patchText = @"*** Add File: ../../../etc/passwd
+malicious content";

            var parameters = new Dictionary<string, object>
            {
                { "patchText", patchText }
            };

            var result = await _tool.ExecuteAsync(parameters);

            result.Success.Should().BeFalse();
            result.Error.Should().Contain("Path traversal");
        }

        [Fact]
        public async Task ExecuteAsync_InvalidFilePath_ReturnsError()
        {
            var patchText = @"*** Add File: 
+content";

            var parameters = new Dictionary<string, object>
            {
                { "patchText", patchText }
            };

            var result = await _tool.ExecuteAsync(parameters);

            result.Success.Should().BeFalse();
        }

        #endregion

        #region Statistics

        [Fact]
        public async Task ExecuteAsync_CalculatesCorrectStatistics()
        {
            _fileHelper.CreateFile("stats.txt", "Line 1\nLine 2\nLine 3");

            var patchText = @"*** Update File: stats.txt
@@ Line 2 @@
-Line 3
+Line 3 Modified
+Line 4 Added";

            var parameters = new Dictionary<string, object>
            {
                { "patchText", patchText }
            };

            var result = await _tool.ExecuteAsync(parameters);

            result.Success.Should().BeTrue();
            result.FormattedOutput.Should().Contain("3 additions");
            result.FormattedOutput.Should().Contain("2 removal");
        }

        [Fact]
        public async Task ExecuteAsync_MultipleFiles_ReportsAllChanges()
        {
            _fileHelper.CreateFile("file1.txt", "Content 1");
            _fileHelper.CreateFile("file2.txt", "Content 2");

            var patchText = @"*** Update File: file1.txt
@@ Content 1 @@
-Content 1
+Modified 1
*** Add File: file3.txt
+New file
*** Delete File: file2.txt";

            var parameters = new Dictionary<string, object>
            {
                { "patchText", patchText }
            };

            var result = await _tool.ExecuteAsync(parameters);

            result.Success.Should().BeTrue();
            result.FormattedOutput.Should().Contain("3 files changed");
        }

        #endregion

        #region Line Endings

        [Fact]
        public async Task ExecuteAsync_PreservesWindowsLineEndings()
        {
            var content = "Line 1\r\nLine 2\r\nLine 3";
            _fileHelper.CreateFile("windows.txt", content);

            var patchText = @"*** Update File: windows.txt
@@ Line 2 @@
 Line 3
+Line 4";

            var parameters = new Dictionary<string, object>
            {
                { "patchText", patchText }
            };

            var result = await _tool.ExecuteAsync(parameters);

            result.Success.Should().BeTrue();
            var updatedContent = _fileHelper.ReadFile("windows.txt");
            
            // Platform-aware line ending check
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                updatedContent.Should().Contain("\r\n");
            }
            else
            {
                // On Unix-based systems, line endings may be normalized to \n
                updatedContent.Should().Contain("\n");
            }
            updatedContent.Should().Contain("Line 4");
        }

        [Fact]
        public async Task ExecuteAsync_PreservesUnixLineEndings()
        {
            var content = "Line 1\nLine 2\nLine 3";
            _fileHelper.CreateFile("unix.txt", content, Encoding.UTF8);
            
            var filePath = _fileHelper.GetPath("unix.txt");
            File.WriteAllText(filePath, content.Replace("\r\n", "\n"));

            var patchText = @"*** Update File: unix.txt
@@ Line 2 @@
 Line 3
+Line 4";

            var parameters = new Dictionary<string, object>
            {
                { "patchText", patchText }
            };

            var result = await _tool.ExecuteAsync(parameters);

            result.Success.Should().BeTrue();
            var updatedContent = File.ReadAllText(filePath);
            
            // Platform-aware line ending check
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Windows may preserve or convert line endings
                updatedContent.Should().Contain("\n");
            }
            else
            {
                // Unix systems should not have Windows line endings
                updatedContent.Should().NotContain("\r\n");
                updatedContent.Should().Contain("\n");
            }
            updatedContent.Should().Contain("Line 4");
        }

        #endregion

        #region Complex Patches

        [Fact]
        public async Task ExecuteAsync_ComplexCodeChange_AppliesCorrectly()
        {
            var originalCode = @"public class Calculator
{
    public int Add(int a, int b)
    {
        return a + b;
    }
    
    public int Subtract(int a, int b)
    {
        return a - b;
    }
}";

            _fileHelper.CreateFile("Calculator.cs", originalCode);

            var patchText = @"*** Update File: Calculator.cs
@@ public int Add(int a, int b) @@
     {
-        return a + b;
+        // Add two numbers
+        var result = a + b;
+        return result;
     }
@@ public int Subtract(int a, int b) @@
     {
         return a - b;
     }
+    
+    public int Multiply(int a, int b)
+    {
+        return a * b;
+    }
 }";

            var parameters = new Dictionary<string, object>
            {
                { "patchText", patchText }
            };

            var result = await _tool.ExecuteAsync(parameters);

            result.Success.Should().BeTrue();
            var updatedCode = _fileHelper.ReadFile("Calculator.cs");
            updatedCode.Should().Contain("// Add two numbers");
            updatedCode.Should().Contain("var result = a + b");
            updatedCode.Should().Contain("public int Multiply");
            updatedCode.Should().Contain("return a * b");
        }

        #endregion

        #region Directory Creation

        [Fact]
        public async Task ExecuteAsync_CreateFileInNewDirectory_CreatesDirectoryStructure()
        {
            var patchText = @"*** Add File: src/components/Button.cs
+public class Button { }";

            var parameters = new Dictionary<string, object>
            {
                { "patchText", patchText }
            };

            var result = await _tool.ExecuteAsync(parameters);

            result.Success.Should().BeTrue();
            Directory.Exists(Path.Combine(_fileHelper.TestDirectory, "src")).Should().BeTrue();
            Directory.Exists(Path.Combine(_fileHelper.TestDirectory, "src/components")).Should().BeTrue();
            _fileHelper.FileExists("src/components/Button.cs").Should().BeTrue();
        }

        #endregion

        #region Tool Metadata

        [Fact]
        public void Name_ReturnsCorrectToolName()
        {
            _tool.Name.Should().Be("apply_diff");
        }

        [Fact]
        public void Description_ReturnsNonEmptyDescription()
        {
            _tool.Description.Should().NotBeNullOrWhiteSpace();
            _tool.Description.Should().Contain("file changes");
        }

        [Fact]
        public void GetParameters_ReturnsExpectedParameters()
        {
            var parameters = _tool.GetParameters();
            
            parameters.Should().ContainKey("properties");
            var properties = parameters["properties"] as Dictionary<string, object>;
            properties.Should().ContainKey("patchText");
            properties.Should().ContainKey("dryRun");
        }

        [Fact]
        public void GetDisplaySummary_ReturnsReadableSummary()
        {
            var patchText = @"*** Add File: new.txt
+Line 1
+Line 2
*** Update File: existing.txt
@@ context @@
-old
+new
*** Delete File: old.txt";

            var parameters = new Dictionary<string, object>
            {
                { "patchText", patchText }
            };

            var summary = _tool.GetDisplaySummary(parameters);
            
            summary.Should().Contain("Patching");
            summary.Should().Contain("3+");  // 2 from new.txt + 1 from existing.txt
            summary.Should().Contain("1-");
        }

        #endregion

        #region Edge Cases

        [Fact]
        public async Task ExecuteAsync_EmptyFile_HandlesCorrectly()
        {
            _fileHelper.CreateFile("empty.txt", "");

            var patchText = @"*** Add File: empty2.txt
+First line";

            var parameters = new Dictionary<string, object>
            {
                { "patchText", patchText }
            };

            var result = await _tool.ExecuteAsync(parameters);

            result.Success.Should().BeTrue();
            _fileHelper.ReadFile("empty2.txt").Should().Be("First line");
        }

        [Fact]
        public async Task ExecuteAsync_SpecialCharactersInPath_HandlesCorrectly()
        {
            var fileName = "file with spaces.txt";
            _fileHelper.CreateFile(fileName, "Content");

            var patchText = $@"*** Update File: {fileName}
@@ Content @@
-Content
+Updated Content";

            var parameters = new Dictionary<string, object>
            {
                { "patchText", patchText }
            };

            var result = await _tool.ExecuteAsync(parameters);

            result.Success.Should().BeTrue();
            _fileHelper.ReadFile(fileName).Should().Be("Updated Content");
        }

        [Fact]
        public async Task ExecuteAsync_InvalidPatchFormat_ReturnsError()
        {
            var patchText = @"This is not a valid patch format
Just some random text";

            var parameters = new Dictionary<string, object>
            {
                { "patchText", patchText }
            };

            var result = await _tool.ExecuteAsync(parameters);

            result.Success.Should().BeTrue();
            result.FormattedOutput.Should().Contain("0 files changed");
        }

        #endregion
    }
}