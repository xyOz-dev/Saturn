using System.Collections.Generic;

namespace Saturn.Tools.Objects
{
    internal class PatchOperation
    {
        public OperationType Type { get; set; }
        public string FilePath { get; set; } = string.Empty;
        public List<PatchHunk> Hunks { get; set; } = new List<PatchHunk>();
        public string Content { get; set; } = string.Empty;
    }
}