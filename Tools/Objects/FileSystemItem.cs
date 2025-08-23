using System;
using System.IO;

namespace Saturn.Tools.Objects
{
    internal class FileSystemItem
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
}