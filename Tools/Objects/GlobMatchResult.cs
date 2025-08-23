using System.Collections.Generic;

namespace Saturn.Tools.Objects
{
    internal class GlobMatchResult
    {
        public List<GlobMatch> Matches { get; set; } = new List<GlobMatch>();
        public int TotalCount { get; set; }
    }
}