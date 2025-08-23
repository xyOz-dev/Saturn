using System.Collections.Generic;

namespace Saturn.Tools.Objects
{
    public class GrepResult
    {
        public string FilePath { get; set; } = string.Empty;
        public int LineNumber { get; set; }
        public string Line { get; set; } = string.Empty;
        public List<MatchInfo> Matches { get; set; } = new List<MatchInfo>();
    }
}