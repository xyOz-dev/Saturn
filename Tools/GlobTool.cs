using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Saturn.Tools.Core;

namespace Saturn.Tools
{
    public class GlobTool : ToolBase
    {
        public override string Name => "glob";
        
        public override string Description => "Find files and directories by name using glob patterns like *.cs or **/*.txt";
        
        protected override Dictionary<string, object> GetParameterProperties()
        {
            return new Dictionary<string, object>
            {
                { "pattern", new Dictionary<string, object>
                    {
                        { "type", "string" },
                        { "description", "Glob pattern to match file names. Use * for any characters, ** for recursive directories, ? for single character" }
                    }
                },
                { "path", new Dictionary<string, object>
                    {
                        { "type", "string" },
                        { "description", "Base directory to search from (defaults to current directory)" }
                    }
                },
                { "includeDirectories", new Dictionary<string, object>
                    {
                        { "type", "boolean" },
                        { "description", "Include directories in results (default: false)" }
                    }
                },
                { "caseSensitive", new Dictionary<string, object>
                    {
                        { "type", "boolean" },
                        { "description", "Case-sensitive matching (default: false)" }
                    }
                },
                { "maxResults", new Dictionary<string, object>
                    {
                        { "type", "integer" },
                        { "description", "Maximum number of results to return (default: 1000)" }
                    }
                }
            };
        }
        
        protected override string[] GetRequiredParameters()
        {
            return new[] { "pattern" };
        }
        
        public override async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters)
        {
            var pattern = GetParameter<string>(parameters, "pattern");
            var basePath = GetParameter<string>(parameters, "path", Directory.GetCurrentDirectory());
            var includeDirectories = GetParameter<bool>(parameters, "includeDirectories", false);
            var caseSensitive = GetParameter<bool>(parameters, "caseSensitive", false);
            var maxResults = GetParameter<int>(parameters, "maxResults", 1000);
            
            if (string.IsNullOrEmpty(pattern))
            {
                return CreateErrorResult("Pattern CANNOT be empty");
            }
            
            if (!Directory.Exists(basePath))
            {
                return CreateErrorResult($"Path NOT found: {basePath}");
            }
            
            try
            {
                var results = await Task.Run(() => FindMatches(pattern, basePath, includeDirectories, caseSensitive, maxResults));
                return FormatResults(results, basePath, pattern);
            }
            catch (Exception ex)
            {
                return CreateErrorResult($"Error during glob search: {ex.Message}");
            }
        }
        
        private List<GlobMatch> FindMatches(string pattern, string basePath, bool includeDirectories, bool caseSensitive, int maxResults)
        {
            var matcher = new Matcher(caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
            
            var patterns = pattern.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                                 .Select(p => p.Trim())
                                 .Where(p => !string.IsNullOrEmpty(p));
            
            foreach (var p in patterns)
            {
                if (p.StartsWith("!"))
                {
                    matcher.AddExclude(p.Substring(1));
                }
                else
                {
                    matcher.AddInclude(p);
                }
            }
            
            var directoryInfo = new DirectoryInfo(basePath);
            var directoryWrapper = new DirectoryInfoWrapper(directoryInfo);
            
            var matchResult = matcher.Execute(directoryWrapper);
            
            var results = new List<GlobMatch>();
            
            foreach (var match in matchResult.Files.Take(maxResults))
            {
                var relativePath = match.Path.Replace('\\', Path.DirectorySeparatorChar);
                var fullPath = Path.GetFullPath(Path.Combine(basePath, relativePath));
                var fileInfo = new FileInfo(fullPath);
                
                if (fileInfo.Exists)
                {
                    results.Add(new GlobMatch
                    {
                        Path = fullPath,
                        RelativePath = relativePath,
                        IsDirectory = false,
                        Size = fileInfo.Length,
                        LastModified = fileInfo.LastWriteTime
                    });
                }
                else if (includeDirectories)
                {
                    var dirInfo = new DirectoryInfo(fullPath);
                    if (dirInfo.Exists)
                    {
                        results.Add(new GlobMatch
                        {
                            Path = fullPath,
                            RelativePath = relativePath,
                            IsDirectory = true,
                            Size = 0,
                            LastModified = dirInfo.LastWriteTime
                        });
                    }
                }
            }
            
            results.Sort((a, b) => string.Compare(a.RelativePath, b.RelativePath, StringComparison.OrdinalIgnoreCase));
            
            return results;
        }
        
        private ToolResult FormatResults(List<GlobMatch> matches, string basePath, string pattern)
        {
            if (matches.Count == 0)
            {
                return CreateSuccessResult(matches, $"No matches found for pattern '{pattern}' in {basePath}");
            }
            
            var lines = new List<string>();
            lines.Add($"Found {matches.Count} match{(matches.Count == 1 ? "" : "es")} for pattern '{pattern}':");
            lines.Add("");
            
            foreach (var match in matches)
            {
                if (match.IsDirectory)
                {
                    lines.Add($"{match.Path} [directory]");
                }
                else
                {
                    lines.Add($"{match.Path}");
                    lines.Add($"  Size: {FormatFileSize(match.Size)}");
                    lines.Add($"  Modified: {match.LastModified:yyyy-MM-dd HH:mm:ss}");
                }
                lines.Add("");
            }
            
            return CreateSuccessResult(matches, string.Join(Environment.NewLine, lines));
        }
        
        private string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double size = bytes;
            int order = 0;
            
            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size /= 1024;
            }
            
            return $"{size:0.##} {sizes[order]}";
        }
        
        public class GlobMatch
        {
            public string Path { get; set; }
            public string RelativePath { get; set; }
            public bool IsDirectory { get; set; }
            public long Size { get; set; }
            public DateTime LastModified { get; set; }
        }
    }
}