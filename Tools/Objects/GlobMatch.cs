using System;

namespace Saturn.Tools.Objects
{
    internal class GlobMatch
    {
        public string Path { get; set; } = string.Empty;
        public string RelativePath { get; set; } = string.Empty;
        public bool IsDirectory { get; set; }
        public long Size { get; set; }
        public DateTime LastModified { get; set; }
        public bool IsSymbolicLink { get; set; }
    }
}