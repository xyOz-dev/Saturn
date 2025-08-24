namespace Saturn.Tools.Objects
{
    internal class PatchChange
    {
        public ChangeType Type { get; set; }
        public string Content { get; set; } = string.Empty;
    }
}