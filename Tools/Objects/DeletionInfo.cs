using System.Collections.Generic;

namespace Saturn.Tools.Objects
{
    internal class DeletionInfo
    {
        public List<DeletionItem> ItemsToDelete { get; set; } = new List<DeletionItem>();
        public int TotalItems { get; set; }
        public long TotalSize { get; set; }
    }
}