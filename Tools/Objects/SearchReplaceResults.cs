using System.Collections.Generic;

namespace Saturn.Tools.Objects
{
    internal class SearchReplaceResults
    {
        public List<FileResult> ProcessedFiles { get; set; } = new List<FileResult>();
        public int TotalFiles { get; set; }
        public int TotalMatches { get; set; }
        public int TotalReplacements { get; set; }
    }
}