using System.Collections.Generic;

namespace Saturn.Tools.Objects
{
    internal class PatchStatistics
    {
        public List<string> ChangedFiles { get; set; } = new List<string>();
        public int Additions { get; set; }
        public int Removals { get; set; }
    }
}