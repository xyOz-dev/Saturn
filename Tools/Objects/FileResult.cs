using System.Collections.Generic;

namespace Saturn.Tools.Objects
{
    internal class FileResult
    {
        public string Path { get; set; } = string.Empty;
        public int MatchCount { get; set; }
        public int ReplacementCount { get; set; }
        public bool Modified { get; set; }
        public List<SearchMatchInfo> Matches { get; set; } = new List<SearchMatchInfo>();
        public string Preview { get; set; } = string.Empty;
    }
}