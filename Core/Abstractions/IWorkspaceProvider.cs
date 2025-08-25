using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Saturn.Core.Abstractions
{
    public interface IWorkspaceProvider
    {
        string WorkspaceId { get; }
        string WorkspacePath { get; }
        WorkspaceType Type { get; }
        
        Task<bool> ExistsAsync(string path);
        Task<string> ReadTextAsync(string path);
        Task<byte[]> ReadBytesAsync(string path);
        Task WriteTextAsync(string path, string content);
        Task WriteBytesAsync(string path, byte[] content);
        Task<bool> DeleteAsync(string path);
        Task<IEnumerable<WorkspaceItem>> ListAsync(string path, bool recursive = false);
        Task<WorkspaceInfo> GetInfoAsync(string path);
        Task<string> GetAbsolutePathAsync(string relativePath);
        Task<bool> CreateDirectoryAsync(string path);
        
        event EventHandler<WorkspaceChangeEventArgs>? WorkspaceChanged;
    }
    
    public enum WorkspaceType
    {
        Local,
        Virtual,
        Remote,
        Container
    }
    
    public class WorkspaceItem
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public bool IsDirectory { get; set; }
        public long Size { get; set; }
        public DateTime Created { get; set; }
        public DateTime Modified { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new();
    }
    
    public class WorkspaceInfo
    {
        public string Path { get; set; } = string.Empty;
        public bool Exists { get; set; }
        public bool IsDirectory { get; set; }
        public bool IsReadOnly { get; set; }
        public long Size { get; set; }
        public DateTime Created { get; set; }
        public DateTime Modified { get; set; }
        public string? MimeType { get; set; }
        public Dictionary<string, object> Attributes { get; set; } = new();
    }
    
    public class WorkspaceChangeEventArgs : EventArgs
    {
        public string Path { get; set; } = string.Empty;
        public WorkspaceChangeType ChangeType { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
    
    public enum WorkspaceChangeType
    {
        Created,
        Modified,
        Deleted,
        Renamed,
        Moved
    }
}