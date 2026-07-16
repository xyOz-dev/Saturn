using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Saturn.Tests.TestHelpers;
using Saturn.Tools;
using Saturn.Tools.Core;
using Xunit;

namespace Saturn.Tests.Tools
{
    [Collection("WorkingDirectory")]
    public class ToolBugRegressionTests : IDisposable
    {
        private readonly FileTestHelper _fileHelper;
        private readonly string _originalDirectory;

        public ToolBugRegressionTests()
        {
            _fileHelper = new FileTestHelper($"ToolBugRegression_{Guid.NewGuid():N}");
            _originalDirectory = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(_fileHelper.TestDirectory);
        }

        public void Dispose()
        {
            Directory.SetCurrentDirectory(_originalDirectory);

            var siblingDirectory = _fileHelper.TestDirectory + "Sibling";
            if (Directory.Exists(siblingDirectory))
            {
                Directory.Delete(siblingDirectory, true);
            }

            _fileHelper.Dispose();
        }

        #region PathSecurity

        [Fact]
        public void PathSecurity_SiblingDirectoryWithSamePrefix_IsRejected()
        {
            var siblingDirectory = _fileHelper.TestDirectory + "Sibling";
            Directory.CreateDirectory(siblingDirectory);

            var act = () => PathSecurity.ValidateInsideWorkingDirectory(
                Path.Combine(siblingDirectory, "escape.txt"));

            act.Should().Throw<System.Security.SecurityException>()
                .WithMessage("*outside the working directory*");
        }

        [Fact]
        public void PathSecurity_PathInsideWorkingDirectory_IsAllowed()
        {
            var act = () => PathSecurity.ValidateInsideWorkingDirectory("sub/dir/file.txt");

            act.Should().NotThrow();
        }

        [Fact]
        public void PathSecurity_ParentTraversal_IsRejected()
        {
            var act = () => PathSecurity.ValidateInsideWorkingDirectory("../outside.txt");

            act.Should().Throw<System.Security.SecurityException>();
        }

        [Fact]
        public async Task WriteFile_SiblingDirectoryWithSamePrefix_IsRejected()
        {
            var siblingDirectory = _fileHelper.TestDirectory + "Sibling";
            Directory.CreateDirectory(siblingDirectory);

            var tool = new WriteFileTool();
            var result = await tool.ExecuteAsync(new Dictionary<string, object>
            {
                { "path", Path.Combine(siblingDirectory, "escape.txt") },
                { "content", "should not be written" }
            });

            result.Success.Should().BeFalse();
            File.Exists(Path.Combine(siblingDirectory, "escape.txt")).Should().BeFalse();
        }

        #endregion

        #region GetParameter conversions

        private class ParameterProbeTool : ToolBase
        {
            public override string Name => "parameter_probe";
            public override string Description => "test helper";
            protected override Dictionary<string, object> GetParameterProperties() => new();
            protected override string[] GetRequiredParameters() => Array.Empty<string>();
            public override Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters) =>
                Task.FromResult(CreateSuccessResult(new object(), "ok"));

            public T Probe<T>(Dictionary<string, object> parameters, string key, T defaultValue = default!) =>
                GetParameter(parameters, key, defaultValue);
        }

        [Fact]
        public void GetParameter_ListOfObjects_ConvertsToStringArray()
        {
            var tool = new ParameterProbeTool();
            var parameters = new Dictionary<string, object>
            {
                { "exclude", new List<object> { "**/generated/**", "bin" } }
            };

            var value = tool.Probe(parameters, "exclude", Array.Empty<string>());

            value.Should().BeEquivalentTo("**/generated/**", "bin");
        }

        [Fact]
        public void GetParameter_ListOfObjects_ConvertsToStringList()
        {
            var tool = new ParameterProbeTool();
            var parameters = new Dictionary<string, object>
            {
                { "items", new List<object> { "a", "b" } }
            };

            var value = tool.Probe(parameters, "items", new List<string>());

            value.Should().Equal("a", "b");
        }

        [Fact]
        public void GetParameter_DoubleValue_ConvertsToNullableInt()
        {
            var tool = new ParameterProbeTool();
            var parameters = new Dictionary<string, object>
            {
                { "startLine", 50.0 }
            };

            var value = tool.Probe<int?>(parameters, "startLine", null);

            value.Should().Be(50);
        }

        #endregion

        #region SearchAndReplace

        [Fact]
        public async Task SearchAndReplace_MatchAtFileStart_DoesNotThrow()
        {
            _fileHelper.CreateFile("start.txt", "using System;\nusing System.IO;\n");

            var tool = new SearchAndReplaceTool();
            var result = await tool.ExecuteAsync(new Dictionary<string, object>
            {
                { "searchPattern", "using System;" },
                { "replacement", "using System.Text;" },
                { "filePattern", "start.txt" }
            });

            result.Success.Should().BeTrue(result.Error);
            _fileHelper.ReadFile("start.txt").Should().StartWith("using System.Text;");
        }

        [Fact]
        public async Task SearchAndReplace_CrlfFile_PreservesLineEndings()
        {
            var content = "line one\r\nline two\r\nline three\r\n";
            _fileHelper.CreateFile("crlf.txt", content);

            var tool = new SearchAndReplaceTool();
            var result = await tool.ExecuteAsync(new Dictionary<string, object>
            {
                { "searchPattern", "two" },
                { "replacement", "2" },
                { "filePattern", "crlf.txt" }
            });

            result.Success.Should().BeTrue(result.Error);
            var written = File.ReadAllText(_fileHelper.GetPath("crlf.txt"));
            written.Should().Be("line one\r\nline 2\r\nline three\r\n");
            written.Should().NotContain("\r\r\n");
        }

        [Fact]
        public async Task SearchAndReplace_BomlessFile_DoesNotGainBom()
        {
            File.WriteAllBytes(_fileHelper.GetPath("nobom.txt"), Encoding.UTF8.GetBytes("hello world\n"));

            var tool = new SearchAndReplaceTool();
            var result = await tool.ExecuteAsync(new Dictionary<string, object>
            {
                { "searchPattern", "world" },
                { "replacement", "there" },
                { "filePattern", "nobom.txt" }
            });

            result.Success.Should().BeTrue(result.Error);
            var bytes = File.ReadAllBytes(_fileHelper.GetPath("nobom.txt"));
            bytes.Take(3).Should().NotEqual(new byte[] { 0xEF, 0xBB, 0xBF });
        }

        #endregion

        #region Grep result budget

        [Fact]
        public async Task Grep_LaterFilesStillSearched_WhenEarlierFileHasManyMatches()
        {
            var manyMatches = string.Join("\n", Enumerable.Repeat("match here", 8));
            var fewMatches = string.Join("\n", Enumerable.Repeat("match there", 5));
            _fileHelper.CreateFile("a_many.txt", manyMatches);
            _fileHelper.CreateFile("b_few.txt", fewMatches);

            var tool = new GrepTool();
            var result = await tool.ExecuteAsync(new Dictionary<string, object>
            {
                { "pattern", "match" },
                { "path", "." },
                { "maxResults", 10 }
            });

            result.Success.Should().BeTrue(result.Error);
            var results = result.RawData as System.Collections.IEnumerable;
            results!.Cast<object>().Count().Should().Be(10);
        }

        #endregion

        #region ApplyDiff multiple update sections

        [Fact]
        public async Task ApplyDiff_TwoUpdateSectionsForSameFile_AppliesBoth()
        {
            _fileHelper.CreateFile("multi.txt", "alpha\nbravo\ncharlie\ndelta\n");

            var patchText = @"*** Update File: multi.txt
@@ alpha @@
-bravo
+BRAVO
*** Update File: multi.txt
@@ charlie @@
-delta
+DELTA";

            var tool = new ApplyDiffTool();
            var result = await tool.ExecuteAsync(new Dictionary<string, object>
            {
                { "patchText", patchText }
            });

            result.Success.Should().BeTrue(result.Error);
            var written = _fileHelper.ReadFile("multi.txt");
            written.Should().Contain("BRAVO");
            written.Should().Contain("DELTA");
        }

        #endregion

        #region ReadFile line counting

        [Fact]
        public async Task ReadFile_WithEndLine_ReportsAccurateTotalLines()
        {
            _fileHelper.CreateLargeFile("large.txt", 100);

            var tool = new ReadFileTool();
            var result = await tool.ExecuteAsync(new Dictionary<string, object>
            {
                { "path", "large.txt" },
                { "startLine", 1 },
                { "endLine", 10 }
            });

            result.Success.Should().BeTrue(result.Error);
            result.FormattedOutput.Should().Contain("10 of 100 total");
        }

        #endregion
    }
}
