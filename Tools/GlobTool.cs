using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading.Tasks;
using Saturn.Tools.Core;

namespace Saturn.Tools
{
    public class GlobTool : ToolBase
    {
        public override string Name => "glob";
        
        public override string Description => @"Use this tool to find files by name patterns. This is your tool for discovering file locations and understanding project structure.

When to use:
- Finding all files of a certain type (e.g., all .cs files)
- Locating specific files when you know part of the name
- Discovering project structure and organization
- Finding test files, configuration files, or documentation
- Checking if certain files exist in the project

How to use:
- Set 'pattern' to your glob pattern (supports *, ?, ** wildcards)
- Use 'path' to search from a specific directory (optional)
- Set 'includeDirectories' to true to also list folders
- Use 'exclude' array to filter out unwanted results
- Set 'caseSensitive' based on your needs
- Use 'compactOutput' for just file paths
- Set 'maxDepth' to limit recursion depth

Examples:
- Find all C# files: pattern='**/*.cs'
- Find test files: pattern='**/*Test.cs' or pattern='**/test/**/*'
- Find config files: pattern='**/*.{json,xml,config}'
- Find specific file: pattern='**/UserService.cs'
- Find all in folder: pattern='src/models/**/*'

Note: Use this before grep when you need to find files first, then search within them.";
        
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
                },
                { "exclude", new Dictionary<string, object>
                    {
                        { "type", "array" },
                        { "items", new Dictionary<string, object> { { "type", "string" } } },
                        { "description", "Patterns to exclude from results" }
                    }
                },
                { "compactOutput", new Dictionary<string, object>
                    {
                        { "type", "boolean" },
                        { "description", "Show only file paths without size/date info (default: false)" }
                    }
                },
                { "maxDepth", new Dictionary<string, object>
                    {
                        { "type", "integer" },
                        { "description", "Maximum directory depth for recursive search (default: unlimited)" }
                    }
                },
                { "followSymlinks", new Dictionary<string, object>
                    {
                        { "type", "boolean" },
                        { "description", "Follow symbolic links (default: false)" }
                    }
                }
            };
        }
        
        protected override string[] GetRequiredParameters()
        {
            return new[] { "pattern" };
        }
        
        public override string GetDisplaySummary(Dictionary<string, object> parameters)
        {
            var pattern = GetParameter<string>(parameters, "pattern", "");
            var displayPattern = TruncateString(pattern, 40);
            return $"Finding files matching '{displayPattern}'";
        }
        
        public override async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters)
        {
            var pattern = GetParameter<string>(parameters, "pattern");
            var basePath = GetParameter<string>(parameters, "path", Directory.GetCurrentDirectory());
            var includeDirectories = GetParameter<bool>(parameters, "includeDirectories", false);
            var caseSensitive = GetParameter<bool>(parameters, "caseSensitive", false);
            var maxResults = GetParameter<int>(parameters, "maxResults", 1000);
            var exclude = GetParameter<string[]>(parameters, "exclude", Array.Empty<string>());
            var compactOutput = GetParameter<bool>(parameters, "compactOutput", false);
            var maxDepth = GetParameter<int>(parameters, "maxDepth", -1);
            var followSymlinks = GetParameter<bool>(parameters, "followSymlinks", false);
            
            if (string.IsNullOrEmpty(pattern))
            {
                return CreateErrorResult("Pattern CANNOT be empty");
            }
            
            try
            {
                ValidatePathSecurity(basePath);
            }
            catch (SecurityException ex)
            {
                return CreateErrorResult($"Security error: {ex.Message}");
            }
            
            if (!Directory.Exists(basePath))
            {
                return CreateErrorResult($"Path NOT found: {basePath}");
            }
            
            try
            {
                var result = await Task.Run(() => FindMatches(pattern, basePath, includeDirectories, caseSensitive, maxResults, exclude, maxDepth, followSymlinks));
                return FormatResults(result, basePath, pattern, compactOutput, maxResults);
            }
            catch (Exception ex)
            {
                return CreateErrorResult($"Error during glob search: {ex.Message}");
            }
        }
        
        private GlobMatchResult FindMatches(string pattern, string basePath, bool includeDirectories, bool caseSensitive, int maxResults, string[] excludePatterns, int maxDepth, bool followSymlinks)
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
            
            foreach (var excludePattern in excludePatterns ?? Array.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(excludePattern))
                {
                    matcher.AddExclude(excludePattern);
                }
            }
            
            var directoryInfo = new DirectoryInfo(basePath);
            var directoryWrapper = new DirectoryInfoWrapper(directoryInfo);
            
            var matchResult = matcher.Execute(directoryWrapper);
            
            var results = new List<GlobMatch>();
            var totalMatches = matchResult.Files.Count();
            var visitedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            foreach (var match in matchResult.Files)
            {
                if (results.Count >= maxResults)
                {
                    break;
                }
                
                var relativePath = match.Path.Replace('\\', '/');
                
                if (maxDepth >= 0)
                {
                    var depth = relativePath.Count(c => c == '/');
                    if (depth > maxDepth)
                    {
                        continue;
                    }
                }
                
                var fullPath = Path.GetFullPath(Path.Combine(basePath, match.Path));
                var fileInfo = new FileInfo(fullPath);
                
                if (fileInfo.Exists)
                {
                    if (!followSymlinks && IsSymbolicLink(fileInfo))
                    {
                        continue;
                    }
                    
                    var canonicalPath = fileInfo.FullName;
                    if (visitedPaths.Contains(canonicalPath))
                    {
                        continue;
                    }
                    visitedPaths.Add(canonicalPath);
                    
                    results.Add(new GlobMatch
                    {
                        Path = fullPath,
                        RelativePath = relativePath,
                        IsDirectory = false,
                        Size = fileInfo.Length,
                        LastModified = fileInfo.LastWriteTime,
                        IsSymbolicLink = IsSymbolicLink(fileInfo)
                    });
                }
                else if (includeDirectories)
                {
                    var dirInfo = new DirectoryInfo(fullPath);
                    if (dirInfo.Exists)
                    {
                        if (!followSymlinks && IsSymbolicLink(dirInfo))
                        {
                            continue;
                        }
                        
                        var canonicalPath = dirInfo.FullName;
                        if (visitedPaths.Contains(canonicalPath))
                        {
                            continue;
                        }
                        visitedPaths.Add(canonicalPath);
                        
                        results.Add(new GlobMatch
                        {
                            Path = fullPath,
                            RelativePath = relativePath,
                            IsDirectory = true,
                            Size = 0,
                            LastModified = dirInfo.LastWriteTime,
                            IsSymbolicLink = IsSymbolicLink(dirInfo)
                        });
                    }
                }
            }
            
            results.Sort((a, b) => string.Compare(a.RelativePath, b.RelativePath, StringComparison.OrdinalIgnoreCase));
            
            return new GlobMatchResult { Matches = results, TotalCount = totalMatches };
        }
        
        private ToolResult FormatResults(GlobMatchResult result, string basePath, string pattern, bool compactOutput, int maxResults)
        {
            var matches = result.Matches;
            
            if (matches.Count == 0)
            {
                return CreateSuccessResult(matches, $"No matches found for pattern '{pattern}' in {basePath}");
            }
            
            var lines = new List<string>();
            
            var truncated = result.TotalCount > matches.Count;
            if (truncated)
            {
                lines.Add($"Found {result.TotalCount} total matches for pattern '{pattern}' (showing first {matches.Count}):");
            }
            else
            {
                lines.Add($"Found {matches.Count} match{(matches.Count == 1 ? "" : "es")} for pattern '{pattern}':");
            }
            lines.Add("");
            
            if (compactOutput)
            {
                foreach (var match in matches)
                {
                    var suffix = match.IsDirectory ? " [dir]" : "";
                    suffix += match.IsSymbolicLink ? " [symlink]" : "";
                    lines.Add($"{match.Path}{suffix}");
                }
            }
            else
            {
                foreach (var match in matches)
                {
                    if (match.IsDirectory)
                    {
                        var suffix = match.IsSymbolicLink ? " [directory, symlink]" : " [directory]";
                        lines.Add($"{match.Path}{suffix}");
                    }
                    else
                    {
                        var suffix = match.IsSymbolicLink ? " [symlink]" : "";
                        lines.Add($"{match.Path}{suffix}");
                        lines.Add($"  Size: {FormatFileSize(match.Size)}");
                        lines.Add($"  Modified: {match.LastModified:yyyy-MM-dd HH:mm:ss}");
                    }
                    lines.Add("");
                }
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
                throw new SecurityException($"Access denied: Path '{path}' is outside the working directory.");
            }
        }
        
        private bool IsSymbolicLink(FileSystemInfo info)
        {
            return info.Attributes.HasFlag(FileAttributes.ReparsePoint);
        }
        
        public class GlobMatch
        {
            public string Path { get; set; } = string.Empty;
            public string RelativePath { get; set; } = string.Empty;
            public bool IsDirectory { get; set; }
            public long Size { get; set; }
            public DateTime LastModified { get; set; }
            public bool IsSymbolicLink { get; set; }
        }
        
        public class GlobMatchResult
        {
            public List<GlobMatch> Matches { get; set; } = new List<GlobMatch>();
            public int TotalCount { get; set; }
        }
    }
}