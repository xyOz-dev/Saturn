namespace Saturn.Tools.Objects
{
    internal class FileChange
    {
        public ChangeType Type { get; set; }
        public string OldContent { get; set; } = string.Empty;
        public string NewContent { get; set; } = string.Empty;
    }
}