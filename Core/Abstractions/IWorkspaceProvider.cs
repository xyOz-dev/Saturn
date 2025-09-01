using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Saturn.Core.Abstractions
{
    public interface IWorkspaceProvider
    {
        string WorkspaceId { get; }
        string WorkspacePath { get; }
        WorkspaceType Type { get; }
        
        Task<bool> ExistsAsync(string path, CancellationToken cancellation = default);
        Task<string> ReadTextAsync(string path, CancellationToken cancellation = default);
        Task<byte[]> ReadBytesAsync(string path, CancellationToken cancellation = default);
        Task WriteTextAsync(string path, string content, CancellationToken cancellation = default);
        Task WriteBytesAsync(string path, byte[] content, CancellationToken cancellation = default);
        Task<bool> DeleteAsync(string path, CancellationToken cancellation = default);
        Task<IEnumerable<WorkspaceItem>> ListAsync(string path, bool recursive = false, CancellationToken cancellation = default);
        Task<WorkspaceInfo> GetInfoAsync(string path, CancellationToken cancellation = default);
        Task<string> GetAbsolutePathAsync(string relativePath, CancellationToken cancellation = default);
        Task<bool> CreateDirectoryAsync(string path, CancellationToken cancellation = default);
        
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
        public string OldPath { get; set; } = string.Empty;
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