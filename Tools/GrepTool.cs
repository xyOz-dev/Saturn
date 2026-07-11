using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Saturn.Tools.Core;
using Saturn.Tools.Objects;

namespace Saturn.Tools
{
    public class GrepTool : ToolBase
    {
        public override string Name => "grep";
        
        public override string Description => @"Use this tool to search for text patterns inside files. This is your primary tool for finding code, comments, or any text content across the codebase.

When to use:
- Finding function/class definitions (e.g., search for 'class UserController')
- Locating specific code patterns or implementations
- Finding TODO comments or specific annotations
- Searching for error messages or string literals
- Finding all usages of a particular method or variable

How to use:
- Set 'pattern' to your regex search term (required, supports full regex syntax)
- Set 'path' to the file or directory to search in (required, e.g. '.' for the current directory)
- Set 'recursive' to true to search subdirectories - the default is false, so without it only the top level of 'path' is searched
- Use 'filePattern' to search only certain file types (e.g., '*.cs')
- Set 'caseSensitive' to false for case-insensitive search (default is true)
- Use 'maxResults' to limit output for broad searches (default 1000)

Examples:
- Find a class anywhere in the project: pattern='class\s+UserService', path='.', recursive=true
- Find TODOs in C# files: pattern='TODO|FIXME|HACK', path='.', recursive=true, filePattern='*.cs'
- Find a method in one file: pattern='GetUserById\s*\(', path='src/UserService.cs'";
        
        protected override Dictionary<string, object> GetParameterProperties()
        {
            return new Dictionary<string, object>
            {
                { "pattern", new Dictionary<string, object>
                    {
                        { "type", "string" },
                        { "description", "The regular expression pattern to search for" }
                    }
                },
                { "path", new Dictionary<string, object>
                    {
                        { "type", "string" },
                        { "description", "File or directory path to search in. Use '.' for the current directory" }
                    }
                },
                { "recursive", new Dictionary<string, object>
                    {
                        { "type", "boolean" },
                        { "default", false },
                        { "description", "Search subdirectories recursively. Default is false (only the top level of 'path' is searched)" }
                    }
                },
                { "filePattern", new Dictionary<string, object>
                    {
                        { "type", "string" },
                        { "default", "*" },
                        { "description", "File name pattern to filter which files to search in. Default is * for all files" }
                    }
                },
                { "caseSensitive", new Dictionary<string, object>
                    {
                        { "type", "boolean" },
                        { "default", true },
                        { "description", "Match case exactly. Set to false for case-insensitive search. Default is true" }
                    }
                },
                { "maxResults", new Dictionary<string, object>
                    {
                        { "type", "integer" },
                        { "default", 1000 },
                        { "description", "Maximum number of results to return. Default is 1000" }
                    }
                }
            };
        }
        
        protected override string[] GetRequiredParameters()
        {
            return new[] { "pattern", "path" };
        }
        
        public override string GetDisplaySummary(Dictionary<string, object> parameters)
        {
            var pattern = GetParameter<string>(parameters, "pattern", "");
            var path = GetParameter<string>(parameters, "path", "");
            var displayPattern = TruncateString(pattern, 30);
            var displayPath = FormatPath(path);
            return $"Searching for '{displayPattern}' in {displayPath}";
        }
        
        public override async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters)
        {
            var pattern = GetParameter<string>(parameters, "pattern");
            var path = GetParameter<string>(parameters, "path");
            var recursive = GetParameter<bool>(parameters, "recursive", false);
            var filePattern = GetParameter<string>(parameters, "filePattern", "*");
            var caseSensitive = GetParameter<bool>(parameters, "caseSensitive", true);
            var maxResults = GetParameter<int>(parameters, "maxResults", 1000);
            
            if (string.IsNullOrEmpty(pattern))
            {
                return CreateErrorResult("Pattern CANNOT be empty");
            }
            
            if (string.IsNullOrEmpty(path))
            {
                return CreateErrorResult("Path CANNOT be empty");
            }
            
            var results = new List<GrepResult>();
            var regexOptions = caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
            Regex regex;
            try
            {
                regex = new Regex(pattern, regexOptions, TimeSpan.FromSeconds(5));
            }
            catch (ArgumentException ex)
            {
                return CreateErrorResult($"Invalid regex pattern: {ex.Message}");
            }
            
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                return CreateErrorResult($"Path NOT found: {path}");
            }
            
            await Task.Run(() =>
            {
                if (File.Exists(path))
                {
                    SearchFile(path, regex, results, maxResults);
                }
                else if (Directory.Exists(path))
                {
                    var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                    var files = Directory.GetFiles(path, filePattern, searchOption);
                    
                    foreach (var file in files)
                    {
                        if (results.Count >= maxResults)
                            break;
                        
                        SearchFile(file, regex, results, maxResults - results.Count);
                    }
                }
            });
            
            return FormatResults(results);
        }
        
        private void SearchFile(string filePath, Regex regex, List<GrepResult> results, int maxResults)
        {
            try
            {
                var lines = File.ReadAllLines(filePath);
                for (int i = 0; i < lines.Length && results.Count < maxResults; i++)
                {
                    var matches = regex.Matches(lines[i]);
                    if (matches.Count > 0)
                    {
                        results.Add(new GrepResult
                        {
                            FilePath = filePath,
                            LineNumber = i + 1,
                            Line = lines[i],
                            Matches = matches.Cast<Match>().Select(m => new MatchInfo
                            {
                                Value = m.Value,
                                Index = m.Index,
                                Length = m.Length
                            }).ToList()
                        });
                    }
                }
            }
            catch (Exception ex)
            {

            }
        }
        
        private ToolResult FormatResults(List<GrepResult> results)
        {
            if (results.Count == 0)
            {
                return CreateSuccessResult(results, "No matches found.");
            }
            
            var lines = new List<string>();
            lines.Add($"Found {results.Count} match{(results.Count == 1 ? "" : "es")}:");
            lines.Add("");
            
            foreach (var result in results)
            {
                lines.Add($"{result.FilePath}:{result.LineNumber}");
                lines.Add($"  {result.Line.Trim()}");
                
                if (result.Matches?.Count > 0)
                {
                    var matchInfo = string.Join(", ", result.Matches.Select(m => $"'{m.Value}' at position {m.Index}"));
                    lines.Add($"  Matches: {matchInfo}");
                }
                lines.Add("");
            }
            
            return CreateSuccessResult(results, string.Join(Environment.NewLine, lines));
        }
    }
}