using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using Saturn.Tools.Core;
using Saturn.Tools.Objects;

namespace Saturn.Tools
{
    public class SearchAndReplaceTool : ToolBase
    {
        private const int MaxFileSize = 10 * 1024 * 1024;
        private const int MaxFilesPerOperation = 1000;
        
        public override string Name => "search_and_replace";
        
        public override string Description => @"Search and replace text across multiple files with regex support.

When to use:
- Renaming variables/functions across codebase
- Updating imports/namespaces
- Changing configuration values
- Fixing consistent typos
- Refactoring code patterns
- Updating copyright headers

How to use:
- Set 'searchPattern' as text or regex to find
- Provide 'replacement' text
- Use 'filePattern' to target specific files
- Enable 'regex' for pattern matching
- Use 'caseSensitive' for exact matching

Safety features:
- Dry run mode for preview
- File size limits
- Encoding preservation";
        
        protected override Dictionary<string, object> GetParameterProperties()
        {
            return new Dictionary<string, object>
            {
                { "searchPattern", new Dictionary<string, object>
                    {
                        { "type", "string" },
                        { "description", "Text or regex pattern to search for" }
                    }
                },
                { "replacement", new Dictionary<string, object>
                    {
                        { "type", "string" },
                        { "description", "Replacement text (supports regex groups like $1)" }
                    }
                },
                { "filePattern", new Dictionary<string, object>
                    {
                        { "type", "string" },
                        { "description", "Glob pattern for files to process (e.g., **/*.cs)" }
                    }
                },
                { "path", new Dictionary<string, object>
                    {
                        { "type", "string" },
                        { "description", "Base directory to search from (default: current)" }
                    }
                },
                { "regex", new Dictionary<string, object>
                    {
                        { "type", "boolean" },
                        { "description", "Use regex for search pattern (default: false)" }
                    }
                },
                { "caseSensitive", new Dictionary<string, object>
                    {
                        { "type", "boolean" },
                        { "description", "Case-sensitive search (default: true)" }
                    }
                },
                { "wholeWord", new Dictionary<string, object>
                    {
                        { "type", "boolean" },
                        { "description", "Match whole words only (default: false)" }
                    }
                },
                { "exclude", new Dictionary<string, object>
                    {
                        { "type", "array" },
                        { "items", new Dictionary<string, object> { { "type", "string" } } },
                        { "description", "Patterns to exclude from search" }
                    }
                },
                { "dryRun", new Dictionary<string, object>
                    {
                        { "type", "boolean" },
                        { "description", "Preview changes without applying (default: false)" }
                    }
                },
                { "maxFiles", new Dictionary<string, object>
                    {
                        { "type", "integer" },
                        { "description", "Maximum files to process (default: 1000)" }
                    }
                }
            };
        }
        
        protected override string[] GetRequiredParameters()
        {
            return new[] { "searchPattern", "replacement", "filePattern" };
        }
        
        public override string GetDisplaySummary(Dictionary<string, object> parameters)
        {
            var searchPattern = GetParameter<string>(parameters, "searchPattern", "");
            var replacement = GetParameter<string>(parameters, "replacement", "");
            var filePattern = GetParameter<string>(parameters, "filePattern", "");
            
            var oldText = TruncateString(searchPattern, 20);
            var newText = TruncateString(replacement, 20);
            var files = TruncateString(filePattern, 20);
            
            return $"Replacing '{oldText}' → '{newText}' in {files}";
        }
        
        public override async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters)
        {
            var searchPattern = GetParameter<string>(parameters, "searchPattern");
            var replacement = GetParameter<string>(parameters, "replacement");
            var filePattern = GetParameter<string>(parameters, "filePattern");
            var basePath = GetParameter<string>(parameters, "path", Directory.GetCurrentDirectory());
            var useRegex = GetParameter<bool>(parameters, "regex", false);
            var caseSensitive = GetParameter<bool>(parameters, "caseSensitive", true);
            var wholeWord = GetParameter<bool>(parameters, "wholeWord", false);
            var exclude = GetParameter<string[]>(parameters, "exclude", Array.Empty<string>());
            var dryRun = GetParameter<bool>(parameters, "dryRun", false);
            var maxFiles = GetParameter<int>(parameters, "maxFiles", MaxFilesPerOperation);
            
            if (string.IsNullOrEmpty(searchPattern))
            {
                return CreateErrorResult("Search pattern cannot be empty");
            }
            
            if (replacement == null)
            {
                replacement = "";
            }
            
            try
            {
                ValidatePathSecurity(basePath);
                
                var files = await FindMatchingFiles(filePattern, basePath, exclude, maxFiles);
                
                if (files.Count == 0)
                {
                    return CreateSuccessResult(new { Files = 0 }, $"No files found matching pattern '{filePattern}'");
                }
                
                var regex = BuildSearchRegex(searchPattern, useRegex, caseSensitive, wholeWord);
                var results = await ProcessFiles(files, regex, replacement, dryRun);
                
                if (dryRun)
                {
                    return FormatDryRunResults(results);
                }
                
                return FormatResults(results);
            }
            catch (Exception ex)
            {
                return CreateErrorResult($"Search and replace failed: {ex.Message}");
            }
        }
        
        private async Task<List<string>> FindMatchingFiles(string pattern, string basePath, string[] excludePatterns, int maxFiles)
        {
            var matcher = new Matcher();
            matcher.AddInclude(pattern);
            
            foreach (var excludePattern in excludePatterns)
            {
                if (!string.IsNullOrWhiteSpace(excludePattern))
                {
                    matcher.AddExclude(excludePattern);
                }
            }
            
            var matchResult = matcher.Execute(new DirectoryInfoWrapper(new DirectoryInfo(basePath)));
            var files = new List<string>();
            
            foreach (var match in matchResult.Files.Take(maxFiles))
            {
                var fullPath = Path.GetFullPath(Path.Combine(basePath, match.Path));
                var fileInfo = new FileInfo(fullPath);
                
                if (fileInfo.Exists && fileInfo.Length <= MaxFileSize)
                {
                    files.Add(fullPath);
                }
            }
            
            return await Task.FromResult(files);
        }
        
        private Regex BuildSearchRegex(string pattern, bool useRegex, bool caseSensitive, bool wholeWord)
        {
            var regexPattern = pattern;
            
            if (!useRegex)
            {
                regexPattern = Regex.Escape(pattern);
            }
            
            if (wholeWord)
            {
                regexPattern = $@"\b{regexPattern}\b";
            }
            
            var options = RegexOptions.Multiline;
            if (!caseSensitive)
            {
                options |= RegexOptions.IgnoreCase;
            }
            
            return new Regex(regexPattern, options);
        }
        
        private async Task<SearchReplaceResults> ProcessFiles(List<string> files, Regex searchRegex, string replacement, bool dryRun)
        {
            var results = new SearchReplaceResults
            {
                ProcessedFiles = new List<FileResult>()
            };
            
            foreach (var file in files)
            {
                var fileResult = await ProcessSingleFile(file, searchRegex, replacement, dryRun);
                if (fileResult.MatchCount > 0)
                {
                    results.ProcessedFiles.Add(fileResult);
                }
            }
            
            results.TotalFiles = results.ProcessedFiles.Count;
            results.TotalMatches = results.ProcessedFiles.Sum(f => f.MatchCount);
            results.TotalReplacements = results.ProcessedFiles.Sum(f => f.ReplacementCount);
            
            return results;
        }
        
        private async Task<FileResult> ProcessSingleFile(string filePath, Regex searchRegex, string replacement, bool dryRun)
        {
            var result = new FileResult
            {
                Path = filePath,
                Matches = new List<SearchMatchInfo>()
            };
            
            var content = await File.ReadAllTextAsync(filePath);
            var originalContent = content;
            var encoding = DetectEncoding(filePath);
            var lineEnding = DetectLineEnding(content);
            
            var matches = searchRegex.Matches(content);
            result.MatchCount = matches.Count;
            
            if (matches.Count == 0)
            {
                return result;
            }
            
            foreach (Match match in matches)
            {
                var lineNumber = GetLineNumber(content, match.Index);
                var lineContent = GetLineContent(content, match.Index);
                
                result.Matches.Add(new SearchMatchInfo
                {
                    Line = lineNumber,
                    Column = GetColumnNumber(content, match.Index),
                    MatchedText = match.Value,
                    LineContent = lineContent
                });
            }
            
            if (!dryRun)
            {
                var newContent = searchRegex.Replace(content, replacement);
                
                if (newContent != originalContent)
                {
                    if (lineEnding != Environment.NewLine)
                    {
                        newContent = newContent.Replace(Environment.NewLine, lineEnding);
                    }
                    
                    await File.WriteAllTextAsync(filePath, newContent, encoding);
                    result.ReplacementCount = matches.Count;
                    result.Modified = true;
                }
            }
            else
            {
                result.ReplacementCount = matches.Count;
                
                var newContent = searchRegex.Replace(content, replacement);
                if (newContent != originalContent)
                {
                    result.Preview = GeneratePreview(originalContent, newContent, result.Matches);
                }
            }
            
            return result;
        }
        
        private int GetLineNumber(string content, int index)
        {
            return content.Take(index).Count(c => c == '\n') + 1;
        }
        
        private int GetColumnNumber(string content, int index)
        {
            var lastNewline = content.LastIndexOf('\n', index - 1);
            return index - lastNewline;
        }
        
        private string GetLineContent(string content, int index)
        {
            var start = content.LastIndexOf('\n', index - 1) + 1;
            var end = content.IndexOf('\n', index);
            if (end == -1) end = content.Length;
            return content.Substring(start, end - start);
        }
        
        private string GeneratePreview(string original, string modified, List<SearchMatchInfo> matches)
        {
            var preview = new StringBuilder();
            var maxMatches = Math.Min(matches.Count, 3);
            
            for (int i = 0; i < maxMatches; i++)
            {
                var match = matches[i];
                preview.AppendLine($"  Line {match.Line}:");
                preview.AppendLine($"    - {match.LineContent}");
                
                var modifiedLine = match.LineContent.Replace(match.MatchedText, $"[{match.MatchedText}]");
                preview.AppendLine($"    + {modifiedLine}");
            }
            
            if (matches.Count > 3)
            {
                preview.AppendLine($"  ... and {matches.Count - 3} more matches");
            }
            
            return preview.ToString();
        }
        
        private ToolResult FormatResults(SearchReplaceResults results)
        {
            var output = new StringBuilder();
            output.AppendLine($"Processed {results.TotalFiles} files with {results.TotalReplacements} replacements");
            
            output.AppendLine("\nModified files:");
            foreach (var file in results.ProcessedFiles.Where(f => f.Modified))
            {
                var relativePath = Path.GetRelativePath(Directory.GetCurrentDirectory(), file.Path);
                output.AppendLine($"  {relativePath} ({file.ReplacementCount} replacements)");
            }
            
            var result = new
            {
                TotalFiles = results.TotalFiles,
                TotalMatches = results.TotalMatches,
                TotalReplacements = results.TotalReplacements,
                Files = results.ProcessedFiles.Select(f => new
                {
                    Path = Path.GetRelativePath(Directory.GetCurrentDirectory(), f.Path),
                    Matches = f.MatchCount,
                    Replacements = f.ReplacementCount
                }).ToList()
            };
            
            return CreateSuccessResult(result, output.ToString());
        }
        
        private ToolResult FormatDryRunResults(SearchReplaceResults results)
        {
            var output = new StringBuilder();
            output.AppendLine($"[DRY RUN] Would process {results.TotalFiles} files with {results.TotalMatches} matches");
            output.AppendLine("\nFiles with matches:");
            
            foreach (var file in results.ProcessedFiles)
            {
                var relativePath = Path.GetRelativePath(Directory.GetCurrentDirectory(), file.Path);
                output.AppendLine($"\n{relativePath} ({file.MatchCount} matches):");
                
                if (!string.IsNullOrEmpty(file.Preview))
                {
                    output.Append(file.Preview);
                }
            }
            
            var result = new
            {
                DryRun = true,
                TotalFiles = results.TotalFiles,
                TotalMatches = results.TotalMatches,
                Files = results.ProcessedFiles.Select(f => new
                {
                    Path = Path.GetRelativePath(Directory.GetCurrentDirectory(), f.Path),
                    Matches = f.MatchCount
                }).ToList()
            };
            
            return CreateSuccessResult(result, output.ToString());
        }
        
        private void ValidatePathSecurity(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Path cannot be null or empty");
            }
            
            var fullPath = Path.GetFullPath(path);
            var currentDirectory = Path.GetFullPath(Directory.GetCurrentDirectory());
            
            if (!fullPath.StartsWith(currentDirectory, StringComparison.OrdinalIgnoreCase))
            {
                throw new SecurityException($"Access denied: Path '{path}' is outside the working directory");
            }
        }
        
        private Encoding DetectEncoding(string filePath)
        {
            using var reader = new StreamReader(filePath, Encoding.UTF8, true);
            reader.Peek();
            return reader.CurrentEncoding;
        }
        
        private string DetectLineEnding(string content)
        {
            if (content.Contains("\r\n"))
                return "\r\n";
            if (content.Contains("\n"))
                return "\n";
            return Environment.NewLine;
        }
    }
}