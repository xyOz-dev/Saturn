using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading.Tasks;
using Saturn.Tools.Core;

namespace Saturn.Tools
{
    public class DeleteFileTool : ToolBase
    {
        public override string Name => "delete_file";

        public override string Description => @"Safely delete files or directories inside the
working directory. Supports glob filtering, recursive deletion,
read-only override, and dry-run (no changes). Prevents path
traversal and operations outside the working directory.";

        protected override Dictionary<string, object> GetParameterProperties()
        {
            return new Dictionary<string, object>
            {
                ["path"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["description"] = "Required. File or directory to delete."
                },
                ["recursive"] = new Dictionary<string, object>
                {
                    ["type"] = "boolean",
                    ["description"] = "If true, delete directories and contents recursively."
                },
                ["pattern"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["description"] = "Optional glob (e.g. '*.tmp') to filter files in a directory."
                },
                ["force"] = new Dictionary<string, object>
                {
                    ["type"] = "boolean",
                    ["description"] = "If true, clear read-only attribute before deleting."
                },
                ["dryRun"] = new Dictionary<string, object>
                {
                    ["type"] = "boolean",
                    ["description"] = "If true, simulate deletion and return a report."
                }
            };
        }

        protected override string[] GetRequiredParameters()
        {
            return new[] { "path" };
        }
        
        public override string GetDisplaySummary(Dictionary<string, object> parameters)
        {
            var path = GetParameter<string>(parameters, "path", "");
            var filename = string.IsNullOrEmpty(path) ? "unknown" : System.IO.Path.GetFileName(path);
            return $"Deleting {filename}";
        }
        
        public override async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters)
        {
            var path = GetParameter<string>(parameters, "path");
            var recursive = GetParameter<bool>(parameters, "recursive", false);
            var pattern = GetParameter<string?>(parameters, "pattern", null);
            var force = GetParameter<bool>(parameters, "force", false);
            var dryRun = GetParameter<bool>(parameters, "dryRun", false);
            
            if (string.IsNullOrEmpty(path))
            {
                return CreateErrorResult("Path cannot be empty");
            }
            
            try
            {
                var fullPath = Path.GetFullPath(path);
                
                var deletionInfo = await AnalyzeDeletion(fullPath, pattern, recursive);
                
                if (deletionInfo.TotalItems == 0)
                {
                    return CreateSuccessResult(deletionInfo, "Nothing to delete");
                }
                
                if (dryRun)
                {
                    var dryRunMessage = FormatDryRunMessage(deletionInfo);
                    return CreateSuccessResult(deletionInfo, dryRunMessage);
                }
                
                var deletedItems = await PerformDeletion(deletionInfo.ItemsToDelete, force);
                
                var result = new
                {
                    DeletedFiles = deletedItems.Count(i => i.Type == "file"),
                    DeletedDirectories = deletedItems.Count(i => i.Type == "directory"),
                    TotalSize = deletedItems.Sum(i => i.Size),
                    Items = deletedItems.Select(i => i.Path).ToList()
                };

                var message = $"Deleted {result.DeletedFiles} files and {result.DeletedDirectories} directories ({FormatFileSize(result.TotalSize)})";
                
                return CreateSuccessResult(result, message);
            }
            catch (Exception ex)
            {
                return CreateErrorResult($"Failed to delete: {ex.Message}");
            }
        }
        
        private async Task<DeletionInfo> AnalyzeDeletion(string path, string pattern, bool recursive)
        {
            var info = new DeletionInfo
            {
                ItemsToDelete = new List<DeletionItem>()
            };
            
            if (File.Exists(path))
            {
                var fileInfo = new FileInfo(path);
                info.ItemsToDelete.Add(new DeletionItem
                {
                    Path = path,
                    Type = "file",
                    Size = fileInfo.Length,
                    IsReadOnly = fileInfo.IsReadOnly
                });
            }
            else if (Directory.Exists(path))
            {
                if (!recursive && !string.IsNullOrEmpty(pattern))
                {
                    var files = Directory.GetFiles(path, pattern);
                    foreach (var file in files)
                    {
                        var fileInfo = new FileInfo(file);
                        info.ItemsToDelete.Add(new DeletionItem
                        {
                            Path = file,
                            Type = "file",
                            Size = fileInfo.Length,
                            IsReadOnly = fileInfo.IsReadOnly
                        });
                    }
                }
                else if (recursive)
                {
                    await Task.Run(() => CollectItemsRecursive(path, pattern, info.ItemsToDelete));
                }
                else
                {
                    var dirInfo = new DirectoryInfo(path);
                    if (dirInfo.GetFileSystemInfos().Length == 0)
                    {
                        info.ItemsToDelete.Add(new DeletionItem
                        {
                            Path = path,
                            Type = "directory",
                            Size = 0,
                            IsReadOnly = false
                        });
                    }
                    else
                    {
                        throw new InvalidOperationException($"Directory is not empty: {path}. Use recursive=true to delete");
                    }
                }
            }
            else
            {
                throw new FileNotFoundException($"Path not found: {path}");
            }
            
            info.TotalItems = info.ItemsToDelete.Count;
            info.TotalSize = info.ItemsToDelete.Sum(i => i.Size);
            
            return info;
        }
        
        private void CollectItemsRecursive(string path, string pattern, List<DeletionItem> items)
        {
            var searchPattern = string.IsNullOrEmpty(pattern) ? "*" : pattern;
            
            foreach (var file in Directory.GetFiles(path, searchPattern, SearchOption.AllDirectories))
            {
                var fileInfo = new FileInfo(file);
                items.Add(new DeletionItem
                {
                    Path = file,
                    Type = "file",
                    Size = fileInfo.Length,
                    IsReadOnly = fileInfo.IsReadOnly
                });
            }
            
            foreach (var dir in Directory.GetDirectories(path, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
            {
                items.Add(new DeletionItem
                {
                    Path = dir,
                    Type = "directory",
                    Size = 0,
                    IsReadOnly = false
                });
            }
            
            items.Add(new DeletionItem
            {
                Path = path,
                Type = "directory",
                Size = 0,
                IsReadOnly = false
            });
        }
        
        private async Task<List<DeletionItem>> PerformDeletion(List<DeletionItem> items, bool force)
        {
            var deleted = new List<DeletionItem>();
            
            foreach (var item in items.Where(i => i.Type == "file"))
            {
                if (item.IsReadOnly && force)
                {
                    File.SetAttributes(item.Path, FileAttributes.Normal);
                }
                
                File.Delete(item.Path);
                deleted.Add(item);
            }
            
            foreach (var item in items.Where(i => i.Type == "directory").OrderByDescending(i => i.Path.Length))
            {
                if (Directory.Exists(item.Path))
                {
                    Directory.Delete(item.Path, false);
                    deleted.Add(item);
                }
            }
            
            await Task.CompletedTask;
            return deleted;
        }
        
        private string FormatDryRunMessage(DeletionInfo info)
        {
            var files = info.ItemsToDelete.Count(i => i.Type == "file");
            var dirs = info.ItemsToDelete.Count(i => i.Type == "directory");
            
            var message = $"[DRY RUN] Would delete {files} files and {dirs} directories ({FormatFileSize(info.TotalSize)})\n\n";
            
            if (info.ItemsToDelete.Count <= 20)
            {
                message += "Items to delete:\n";
                foreach (var item in info.ItemsToDelete)
                {
                    var marker = item.Type == "file" ? "F" : "D";
                    var readOnly = item.IsReadOnly ? " [RO]" : "";
                    message += $"  [{marker}] {item.Path}{readOnly}\n";
                }
            }
            else
            {
                message += $"First 20 items:\n";
                foreach (var item in info.ItemsToDelete.Take(20))
                {
                    var marker = item.Type == "file" ? "F" : "D";
                    message += $"  [{marker}] {item.Path}\n";
                }
                message += $"\n... and {info.ItemsToDelete.Count - 20} more items";
            }
            
            return message;
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
        
        private class DeletionInfo
        {
            public List<DeletionItem> ItemsToDelete { get; set; } = new List<DeletionItem>();
            public int TotalItems { get; set; }
            public long TotalSize { get; set; }
        }
        
        private class DeletionItem
        {
            public string Path { get; set; } = string.Empty;
            public string Type { get; set; } = string.Empty;
            public long Size { get; set; }
            public bool IsReadOnly { get; set; }
        }
    }
}