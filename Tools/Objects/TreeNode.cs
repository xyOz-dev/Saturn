using System.Collections.Generic;

namespace Saturn.Tools.Objects
{
    internal class TreeNode
    {
        public string Name { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public bool IsDirectory { get; set; }
        public FileSystemItem Item { get; set; } = new FileSystemItem();
        public List<TreeNode> Children { get; set; } = new List<TreeNode>();
    }
}