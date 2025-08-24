using System.Collections.Generic;

namespace Saturn.Tools.Objects
{
    internal class Commit
    {
        public Dictionary<string, FileChange> Changes { get; set; } = new Dictionary<string, FileChange>();
    }
}