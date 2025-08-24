namespace Saturn.Tools.Objects
{
    internal class SearchMatchInfo
    {
        public int Line { get; set; }
        public int Column { get; set; }
        public string MatchedText { get; set; } = string.Empty;
        public string LineContent { get; set; } = string.Empty;
    }
}