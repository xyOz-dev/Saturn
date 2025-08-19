using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Saturn.Tools.Core;

namespace Saturn.Tools
{
    public class ListFilesTool : ToolBase
    {
        public override string Name => "list_files";

        public override string Description => @"Render a directory as a text tree. Supports recursion, name-only glob
filtering, hidden items, metadata, sorting, depth and result limits.
Secure: rejects '..'/'~' and paths outside the working directory.

Notes:
- pattern matches names only (not paths). When set, only directories whose
  names match are traversed.
- sortBy: name | size | modified | created | type (type = files before dirs).
- maxDepth: root depth = 0; traversal continues while depth < maxDepth.
- The tree prints in path order; sorting mainly affects selection when
  maxResults is applied.";

        protected override Dictionary<string, object> GetParameterProperties()
        {
            return new Dictionary<string, object>
            {
                ["path"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["description"] = "Directory to list (default: current directory)."
                },
                ["recursive"] = new Dictionary<string, object>
                {
                    ["type"] = "boolean",
                    ["description"] = "Include subdirectories (default: false)."
                },
                ["includeHidden"] = new Dictionary<string, object>
                {
                    ["type"] = "boolean",
                    ["description"] = "Include hidden files/directories (default: false)."
                },
                ["pattern"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["description"] =
                        "Name-only glob. Supports *, ?, **, and {a,b}. Examples: '*.cs', " +
                        "'*Test.cs', '*.{cs,ts}'. Does not match paths. When set, only " +
                        "matching directory names are traversed."
                },
                ["sortBy"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["enum"] = new[] { "name", "size", "modified", "created", "type" },
                    ["description"] =
                        "One of: name, size, modified, created, type (files before dirs)."
                },
                ["sortDescending"] = new Dictionary<string, object>
                {
                    ["type"] = "boolean",
                    ["description"] =
                        "Sort descending. Tree prints in path order; sorting mainly " +
                        "affects selection with maxResults."
                },
                ["maxDepth"] = new Dictionary<string, object>
                {
                    ["type"] = "integer",
                    ["description"] =
                        "Maximum recursion depth (root depth = 0; traverse while " +
                        "depth < maxDepth)."
                },
                ["includeMetadata"] = new Dictionary<string, object>
                {
                    ["type"] = "boolean",
                    ["description"] =
                        "Show size, last modified (UTC), and flags (default: false)."
                },
                ["maxResults"] = new Dictionary<string, object>
                {
                    ["type"] = "integer",
                    ["description"] =
                        "Limit items considered after sorting; output tree may be partial."
                },
                ["filesOnly"] = new Dictionary<string, object>
                {
                    ["type"] = "boolean",
                    ["description"] = "Show only files (default: false)."
                },
                ["directoriesOnly"] = new Dictionary<string, object>
                {
                    ["type"] = "boolean",
                    ["description"] =
                        "Show only directories (default: false). Cannot be true with filesOnly."
                }
            };
        }

        protected override string[] GetRequiredParameters()
        {
            return new string[] { };
        }
        
        public override string GetDisplaySummary(Dictionary<string, object> parameters)
        {
            var path = GetParameter<string>(parameters, "path", Directory.GetCurrentDirectory());
            var recursive = GetParameter<bool>(parameters, "recursive", false);
            var displayPath = FormatPath(path);
            return $"Listing {displayPath} (recursive: {recursive})";
        }
        
        public override async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters)
        {
            try
            {
                var path = GetParameter<string>(parameters, "path", Directory.GetCurrentDirectory());
                var recursive = GetParameter<bool>(parameters, "recursive", false);
                var includeHidden = GetParameter<bool>(parameters, "includeHidden", false);
                var pattern = GetParameter<string?>(parameters, "pattern", null);
                var sortBy = GetParameter<string>(parameters, "sortBy", "name");
                var sortDescending = GetParameter<bool>(parameters, "sortDescending", false);
                var maxDepth = GetParameter<int?>(parameters, "maxDepth", null);
                var includeMetadata = GetParameter<bool>(parameters, "includeMetadata", false);
                var maxResults = GetParameter<int?>(parameters, "maxResults", null);
                var filesOnly = GetParameter<bool>(parameters, "filesOnly", false);
                var directoriesOnly = GetParameter<bool>(parameters, "directoriesOnly", false);
                
                if (filesOnly && directoriesOnly)
                {
                    return CreateErrorResult("Cannot specify both filesOnly and directoriesOnly");
                }
                
                ValidatePathSecurity(path);
                var fullPath = Path.GetFullPath(path);
                
                if (!Directory.Exists(fullPath))
                {
                    return CreateErrorResult($"Directory not found: {fullPath}");
                }
                
                var visitedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var items = await EnumerateDirectoryAsync(
                    fullPath, 
                    pattern, 
                    recursive, 
                    includeHidden, 
                    maxDepth, 
                    0, 
                    visitedPaths,
                    filesOnly,
                    directoriesOnly
                );
                
                items = SortItems(items, sortBy, sortDescending);
                
                if (maxResults.HasValue && maxResults.Value > 0)
                {
                    items = items.Take(maxResults.Value).ToList();
                }
                
                var tree = BuildTreeStructure(items, fullPath);
                var output = RenderTree(tree, includeMetadata, fullPath);
                
                var stats = CalculateStatistics(items);
                var result = new
                {
                    Path = fullPath,
                    TotalFiles = stats.FileCount,
                    TotalDirectories = stats.DirectoryCount,
                    TotalSize = stats.TotalSize,
                    ItemCount = items.Count
                };
                
                return CreateSuccessResult(result, output);
            }
            catch (SecurityException ex)
            {
                return CreateErrorResult($"Security error: {ex.Message}");
            }
            catch (Exception ex)
            {
                return CreateErrorResult($"Failed to list files: {ex.Message}");
            }
        }
        
        private void ValidatePathSecurity(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Path cannot be null or empty");
            }
            
            if (path.Contains("..") || path.Contains("~"))
            {
                throw new SecurityException($"Invalid path: {path}. Path traversal attempts are not allowed.");
            }
            
            try
            {
                var fullPath = Path.GetFullPath(path);
                var currentDirectory = Path.GetFullPath(Directory.GetCurrentDirectory());
                
                if (!fullPath.StartsWith(currentDirectory, StringComparison.OrdinalIgnoreCase))
                {
                    throw new SecurityException($"Access denied: Path '{path}' is outside the working directory.");
                }
            }
            catch (Exception ex) when (!(ex is SecurityException))
            {
                throw new ArgumentException($"Invalid path: {path}", ex);
            }
        }
        
        private async Task<List<FileSystemItem>> EnumerateDirectoryAsync(
            string path, 
            string pattern, 
            bool recursive, 
            bool includeHidden,
            int? maxDepth,
            int currentDepth,
            HashSet<string> visitedPaths,
            bool filesOnly,
            bool directoriesOnly)
        {
            var items = new List<FileSystemItem>();
            
            if (maxDepth.HasValue && currentDepth >= maxDepth.Value)
            {
                return items;
            }
            
            try
            {
                var resolvedPath = ResolvePath(path);
                if (!visitedPaths.Add(resolvedPath))
                {
                    return items;
                }
                
                var directoryInfo = new DirectoryInfo(path);
                
                if (!filesOnly)
                {
                    foreach (var dir in directoryInfo.EnumerateDirectories())
                    {
                        try
                        {
                            if (!includeHidden && IsHidden(dir))
                                continue;
                            
                            if (pattern != null && !MatchesPattern(dir.Name, pattern))
                                continue;
                            
                            var item = new FileSystemItem
                            {
                                Name = dir.Name,
                                FullPath = dir.FullName,
                                RelativePath = Path.GetRelativePath(Directory.GetCurrentDirectory(), dir.FullName),
                                IsDirectory = true,
                                Size = 0,
                                CreatedDate = dir.CreationTimeUtc,
                                ModifiedDate = dir.LastWriteTimeUtc,
                                Attributes = dir.Attributes,
                                Depth = currentDepth
                            };
                            
                            items.Add(item);
                            
                            if (recursive)
                            {
                                var subItems = await EnumerateDirectoryAsync(
                                    dir.FullName, 
                                    pattern, 
                                    recursive, 
                                    includeHidden, 
                                    maxDepth, 
                                    currentDepth + 1, 
                                    visitedPaths,
                                    filesOnly,
                                    directoriesOnly
                                );
                                items.AddRange(subItems);
                            }
                        }
                        catch (UnauthorizedAccessException)
                        {
                            items.Add(CreateErrorItem(dir.Name, dir.FullName, "Access Denied", currentDepth));
                        }
                        catch (Exception ex)
                        {
                            items.Add(CreateErrorItem(dir.Name, dir.FullName, ex.Message, currentDepth));
                        }
                    }
                }
                
                if (!directoriesOnly)
                {
                    foreach (var file in directoryInfo.EnumerateFiles())
                    {
                        try
                        {
                            if (!includeHidden && IsHidden(file))
                                continue;
                            
                            if (pattern != null && !MatchesPattern(file.Name, pattern))
                                continue;
                            
                            var item = new FileSystemItem
                            {
                                Name = file.Name,
                                FullPath = file.FullName,
                                RelativePath = Path.GetRelativePath(Directory.GetCurrentDirectory(), file.FullName),
                                IsDirectory = false,
                                Size = file.Length,
                                CreatedDate = file.CreationTimeUtc,
                                ModifiedDate = file.LastWriteTimeUtc,
                                Attributes = file.Attributes,
                                Depth = currentDepth
                            };
                            
                            items.Add(item);
                        }
                        catch (Exception ex)
                        {
                            items.Add(CreateErrorItem(file.Name, file.FullName, ex.Message, currentDepth));
                        }
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                items.Add(CreateErrorItem(Path.GetFileName(path), path, "Access Denied", currentDepth));
            }
            catch (Exception ex)
            {
                items.Add(CreateErrorItem(Path.GetFileName(path), path, ex.Message, currentDepth));
            }
            
            return items;
        }
        
        private string ResolvePath(string path)
        {
            try
            {
                if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
                {
                    return new DirectoryInfo(path).LinkTarget ?? path;
                }
            }
            catch { }
            return path;
        }
        
        private bool IsHidden(FileSystemInfo info)
        {
            if (info.Attributes.HasFlag(FileAttributes.Hidden))
                return true;
            
            if (Environment.OSVersion.Platform == PlatformID.Unix || 
                Environment.OSVersion.Platform == PlatformID.MacOSX)
            {
                return info.Name.StartsWith(".");
            }
            
            return false;
        }
        
        private bool MatchesPattern(string fileName, string pattern)
        {
            if (string.IsNullOrEmpty(pattern))
                return true;
            
            var regexPattern = Regex.Escape(pattern)
                .Replace("\\*\\*", ".*")
                .Replace("\\*", "[^/\\\\]*")
                .Replace("\\?", ".");
            
            regexPattern = regexPattern.Replace("\\{", "{").Replace("\\}", "}");
            regexPattern = Regex.Replace(regexPattern, @"\{([^}]+)\}", m =>
            {
                var options = m.Groups[1].Value.Split(',');
                return "(" + string.Join("|", options.Select(o => Regex.Escape(o.Trim()))) + ")";
            });
            
            return Regex.IsMatch(fileName, $"^{regexPattern}$", RegexOptions.IgnoreCase);
        }
        
        private List<FileSystemItem> SortItems(List<FileSystemItem> items, string sortBy, bool descending)
        {
            IOrderedEnumerable<FileSystemItem> sorted = sortBy.ToLower() switch
            {
                "size" => items.OrderBy(i => i.Size),
                "modified" => items.OrderBy(i => i.ModifiedDate),
                "created" => items.OrderBy(i => i.CreatedDate),
                "type" => items.OrderBy(i => i.IsDirectory).ThenBy(i => Path.GetExtension(i.Name)),
                _ => items.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            };
            
            return descending ? sorted.Reverse().ToList() : sorted.ToList();
        }
        
        private TreeNode BuildTreeStructure(List<FileSystemItem> items, string basePath)
        {
            var root = new TreeNode
            {
                Name = Path.GetFileName(basePath) ?? basePath,
                FullPath = basePath,
                IsDirectory = true,
                Children = new List<TreeNode>()
            };
            
            var nodeMap = new Dictionary<string, TreeNode> { { basePath, root } };
            
            foreach (var item in items.OrderBy(i => i.FullPath))
            {
                var parentPath = Path.GetDirectoryName(item.FullPath);
                if (!nodeMap.TryGetValue(parentPath, out var parentNode))
                {
                    continue;
                }
                
                var node = new TreeNode
                {
                    Name = item.Name,
                    FullPath = item.FullPath,
                    IsDirectory = item.IsDirectory,
                    Item = item,
                    Children = new List<TreeNode>()
                };
                
                parentNode.Children.Add(node);
                
                if (item.IsDirectory)
                {
                    nodeMap[item.FullPath] = node;
                }
            }
            
            return root;
        }
        
        private string RenderTree(TreeNode root, bool includeMetadata, string basePath)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{Path.GetFileName(basePath)}/");
            
            RenderTreeNode(sb, root, "", true, includeMetadata, new List<bool>());
            
            return sb.ToString().TrimEnd();
        }
        
        private void RenderTreeNode(StringBuilder sb, TreeNode node, string prefix, bool isLast, bool includeMetadata, List<bool> levels)
        {
            if (node.Children.Count == 0)
                return;
            
            for (int i = 0; i < node.Children.Count; i++)
            {
                var child = node.Children[i];
                var isLastChild = i == node.Children.Count - 1;
                
                sb.Append(prefix);
                sb.Append(isLastChild ? "└── " : "├── ");
                sb.Append(child.Name);
                
                if (child.IsDirectory)
                {
                    sb.Append("/");
                }
                
                if (includeMetadata && child.Item != null)
                {
                    sb.Append(" ");
                    AppendMetadata(sb, child.Item);
                }
                
                if (child.Item?.HasError == true)
                {
                    sb.Append($" [{child.Item.ErrorMessage}]");
                }
                
                sb.AppendLine();
                
                if (child.Children.Count > 0)
                {
                    var newPrefix = prefix + (isLastChild ? "    " : "│   ");
                    RenderTreeNode(sb, child, newPrefix, isLastChild, includeMetadata, levels);
                }
            }
        }
        
        private void AppendMetadata(StringBuilder sb, FileSystemItem item)
        {
            var metadata = new List<string>();
            
            if (!item.IsDirectory && item.Size >= 0)
            {
                metadata.Add(FormatFileSize(item.Size));
            }
            
            metadata.Add(item.ModifiedDate.ToString("yyyy-MM-dd HH:mm"));
            
            if (item.Attributes.HasFlag(FileAttributes.ReadOnly))
            {
                metadata.Add("readonly");
            }
            
            if (item.Attributes.HasFlag(FileAttributes.Hidden))
            {
                metadata.Add("hidden");
            }
            
            if (metadata.Count > 0)
            {
                sb.Append($"({string.Join(", ", metadata)})");
            }
        }
        
        private string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double size = bytes;
            int order = 0;
            
            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size /= 1024;
            }
            
            return $"{size:0.##} {sizes[order]}";
        }
        
        private FileSystemItem CreateErrorItem(string name, string fullPath, string error, int depth)
        {
            return new FileSystemItem
            {
                Name = name,
                FullPath = fullPath,
                RelativePath = Path.GetRelativePath(Directory.GetCurrentDirectory(), fullPath),
                IsDirectory = true,
                HasError = true,
                ErrorMessage = error,
                Depth = depth,
                Size = -1,
                CreatedDate = DateTime.MinValue,
                ModifiedDate = DateTime.MinValue,
                Attributes = FileAttributes.Normal
            };
        }
        
        private Statistics CalculateStatistics(List<FileSystemItem> items)
        {
            var stats = new Statistics
            {
                FileCount = items.Count(i => !i.IsDirectory && !i.HasError),
                DirectoryCount = items.Count(i => i.IsDirectory && !i.HasError),
                TotalSize = items.Where(i => !i.IsDirectory && !i.HasError).Sum(i => i.Size)
            };
            
            return stats;
        }
        
        private class FileSystemItem
        {
            public string Name { get; set; } = string.Empty;
            public string FullPath { get; set; } = string.Empty;
            public string RelativePath { get; set; } = string.Empty;
            public bool IsDirectory { get; set; }
            public long Size { get; set; }
            public DateTime CreatedDate { get; set; }
            public DateTime ModifiedDate { get; set; }
            public FileAttributes Attributes { get; set; }
            public int Depth { get; set; }
            public bool HasError { get; set; }
            public string? ErrorMessage { get; set; }
        }
        
        private class TreeNode
        {
            public string Name { get; set; } = string.Empty;
            public string FullPath { get; set; } = string.Empty;
            public bool IsDirectory { get; set; }
            public FileSystemItem Item { get; set; } = new FileSystemItem();
            public List<TreeNode> Children { get; set; } = new List<TreeNode>();
        }
        
        private class Statistics
        {
            public int FileCount { get; set; }
            public int DirectoryCount { get; set; }
            public long TotalSize { get; set; }
        }
    }
}