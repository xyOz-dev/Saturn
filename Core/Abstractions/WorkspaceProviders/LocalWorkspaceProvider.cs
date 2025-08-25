using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Saturn.Core.Abstractions.WorkspaceProviders
{
    public class LocalWorkspaceProvider : IWorkspaceProvider
    {
        private readonly string _rootPath;
        private FileSystemWatcher? _watcher;
        
        public string WorkspaceId { get; }
        public string WorkspacePath => _rootPath;
        public WorkspaceType Type => WorkspaceType.Local;
        
        public event EventHandler<WorkspaceChangeEventArgs>? WorkspaceChanged;
        
        public LocalWorkspaceProvider(string? rootPath = null)
        {
            _rootPath = rootPath ?? Directory.GetCurrentDirectory();
            WorkspaceId = $"local_{Path.GetFileName(_rootPath)}_{Guid.NewGuid():N}".Substring(0, 20);
            InitializeWatcher();
        }
        
        private void InitializeWatcher()
        {
            try
            {
                _watcher = new FileSystemWatcher(_rootPath)
                {
                    NotifyFilter = NotifyFilters.FileName | 
                                  NotifyFilters.DirectoryName | 
                                  NotifyFilters.LastWrite | 
                                  NotifyFilters.Size,
                    IncludeSubdirectories = true,
                    EnableRaisingEvents = true
                };
                
                _watcher.Created += (s, e) => OnWorkspaceChanged(e.FullPath, WorkspaceChangeType.Created);
                _watcher.Changed += (s, e) => OnWorkspaceChanged(e.FullPath, WorkspaceChangeType.Modified);
                _watcher.Deleted += (s, e) => OnWorkspaceChanged(e.FullPath, WorkspaceChangeType.Deleted);
                _watcher.Renamed += (s, e) => OnWorkspaceChanged(e.FullPath, WorkspaceChangeType.Renamed);
            }
            catch
            {
                // Watcher initialization failed, continue without file watching
            }
        }
        
        private void OnWorkspaceChanged(string path, WorkspaceChangeType changeType)
        {
            WorkspaceChanged?.Invoke(this, new WorkspaceChangeEventArgs
            {
                Path = path,
                ChangeType = changeType
            });
        }
        
        public Task<bool> ExistsAsync(string path)
        {
            var fullPath = GetFullPath(path);
            return Task.FromResult(File.Exists(fullPath) || Directory.Exists(fullPath));
        }
        
        public async Task<string> ReadTextAsync(string path)
        {
            var fullPath = GetFullPath(path);
            return await File.ReadAllTextAsync(fullPath);
        }
        
        public async Task<byte[]> ReadBytesAsync(string path)
        {
            var fullPath = GetFullPath(path);
            return await File.ReadAllBytesAsync(fullPath);
        }
        
        public async Task WriteTextAsync(string path, string content)
        {
            var fullPath = GetFullPath(path);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            await File.WriteAllTextAsync(fullPath, content);
        }
        
        public async Task WriteBytesAsync(string path, byte[] content)
        {
            var fullPath = GetFullPath(path);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            await File.WriteAllBytesAsync(fullPath, content);
        }
        
        public Task<bool> DeleteAsync(string path)
        {
            try
            {
                var fullPath = GetFullPath(path);
                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                    return Task.FromResult(true);
                }
                else if (Directory.Exists(fullPath))
                {
                    Directory.Delete(fullPath, recursive: true);
                    return Task.FromResult(true);
                }
                return Task.FromResult(false);
            }
            catch
            {
                return Task.FromResult(false);
            }
        }
        
        public Task<IEnumerable<WorkspaceItem>> ListAsync(string path, bool recursive = false)
        {
            var fullPath = GetFullPath(path);
            var items = new List<WorkspaceItem>();
            
            if (!Directory.Exists(fullPath))
                return Task.FromResult<IEnumerable<WorkspaceItem>>(items);
            
            var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            
            foreach (var dir in Directory.GetDirectories(fullPath, "*", searchOption))
            {
                var info = new DirectoryInfo(dir);
                items.Add(new WorkspaceItem
                {
                    Name = info.Name,
                    Path = GetRelativePath(dir),
                    IsDirectory = true,
                    Created = info.CreationTimeUtc,
                    Modified = info.LastWriteTimeUtc
                });
            }
            
            foreach (var file in Directory.GetFiles(fullPath, "*", searchOption))
            {
                var info = new FileInfo(file);
                items.Add(new WorkspaceItem
                {
                    Name = info.Name,
                    Path = GetRelativePath(file),
                    IsDirectory = false,
                    Size = info.Length,
                    Created = info.CreationTimeUtc,
                    Modified = info.LastWriteTimeUtc
                });
            }
            
            return Task.FromResult<IEnumerable<WorkspaceItem>>(items);
        }
        
        public Task<WorkspaceInfo> GetInfoAsync(string path)
        {
            var fullPath = GetFullPath(path);
            
            if (File.Exists(fullPath))
            {
                var fileInfo = new FileInfo(fullPath);
                return Task.FromResult(new WorkspaceInfo
                {
                    Path = path,
                    Exists = true,
                    IsDirectory = false,
                    IsReadOnly = fileInfo.IsReadOnly,
                    Size = fileInfo.Length,
                    Created = fileInfo.CreationTimeUtc,
                    Modified = fileInfo.LastWriteTimeUtc,
                    MimeType = GetMimeType(fileInfo.Extension)
                });
            }
            else if (Directory.Exists(fullPath))
            {
                var dirInfo = new DirectoryInfo(fullPath);
                return Task.FromResult(new WorkspaceInfo
                {
                    Path = path,
                    Exists = true,
                    IsDirectory = true,
                    IsReadOnly = false,
                    Created = dirInfo.CreationTimeUtc,
                    Modified = dirInfo.LastWriteTimeUtc
                });
            }
            
            return Task.FromResult(new WorkspaceInfo
            {
                Path = path,
                Exists = false
            });
        }
        
        public Task<string> GetAbsolutePathAsync(string relativePath)
        {
            return Task.FromResult(GetFullPath(relativePath));
        }
        
        public Task<bool> CreateDirectoryAsync(string path)
        {
            try
            {
                var fullPath = GetFullPath(path);
                Directory.CreateDirectory(fullPath);
                return Task.FromResult(true);
            }
            catch
            {
                return Task.FromResult(false);
            }
        }
        
        private string GetFullPath(string path)
        {
            if (Path.IsPathRooted(path))
                return path;
                
            return Path.GetFullPath(Path.Combine(_rootPath, path));
        }
        
        private string GetRelativePath(string fullPath)
        {
            return Path.GetRelativePath(_rootPath, fullPath);
        }
        
        private string? GetMimeType(string extension)
        {
            return extension.ToLowerInvariant() switch
            {
                ".txt" => "text/plain",
                ".cs" => "text/x-csharp",
                ".json" => "application/json",
                ".xml" => "application/xml",
                ".html" => "text/html",
                ".css" => "text/css",
                ".js" => "text/javascript",
                ".ts" => "text/typescript",
                ".md" => "text/markdown",
                ".pdf" => "application/pdf",
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                ".svg" => "image/svg+xml",
                _ => null
            };
        }
        
        public void Dispose()
        {
            _watcher?.Dispose();
        }
    }
}