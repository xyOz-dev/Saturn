using System.Collections.Generic;

namespace Saturn.Tools.Objects
{
    internal class PatchHunk
    {
        public string ContextLine { get; set; } = string.Empty;
        public List<PatchChange> Changes { get; set; } = new List<PatchChange>();
    }
}