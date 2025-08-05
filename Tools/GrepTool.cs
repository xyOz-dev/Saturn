using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Saturn.Tools.Core;

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
- Set 'pattern' to your regex search term (supports full regex syntax)
- Use 'path' to limit search to specific directory (optional)
- Use 'filePattern' to search only certain file types (e.g., '*.cs')
- Set 'caseSensitive' to false for case-insensitive search
- Set 'recursive' to true to search subdirectories
- Use 'maxResults' to limit output for broad searches

Examples:
- To find a class: pattern='class\\s+UserService'
- To find TODOs: pattern='TODO|FIXME|HACK'
- To find a method: pattern='GetUserById\\s*\\('
- To find imports: pattern='using\\s+System\\.'";
        
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
                        { "description", "File or directory path to search in" }
                    }
                },
                { "recursive", new Dictionary<string, object>
                    {
                        { "type", "boolean" },
                        { "description", "Search recursively in subdirectories" }
                    }
                },
                { "filePattern", new Dictionary<string, object>
                    {
                        { "type", "string" },
                        { "description", "File name pattern to filter which files to search in. Default is * for all files" }
                    }
                },
                { "ignoreCase", new Dictionary<string, object>
                    {
                        { "type", "boolean" },
                        { "description", "Perform case-insensitive search" }
                    }
                },
                { "maxResults", new Dictionary<string, object>
                    {
                        { "type", "integer" },
                        { "description", "Maximum number of results to return" }
                    }
                }
            };
        }
        
        protected override string[] GetRequiredParameters()
        {
            return new[] { "pattern", "path" };
        }
        
        public override async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters)
        {
            var pattern = GetParameter<string>(parameters, "pattern");
            var path = GetParameter<string>(parameters, "path");
            var recursive = GetParameter<bool>(parameters, "recursive", false);
            var filePattern = GetParameter<string>(parameters, "filePattern", "*");
            var ignoreCase = GetParameter<bool>(parameters, "ignoreCase", false);
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
            var regexOptions = ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;
            var regex = new Regex(pattern, regexOptions);
            
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
                Console.WriteLine($"Error reading file {filePath}: {ex.Message}");
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
        
        public class GrepResult
        {
            public string FilePath { get; set; }
            public int LineNumber { get; set; }
            public string Line { get; set; }
            public List<MatchInfo> Matches { get; set; }
        }
        
        public class MatchInfo
        {
            public string Value { get; set; }
            public int Index { get; set; }
            public int Length { get; set; }
        }
    }
}