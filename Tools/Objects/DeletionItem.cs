namespace Saturn.Tools.Objects
{
    internal class DeletionItem
    {
        public string Path { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public long Size { get; set; }
        public bool IsReadOnly { get; set; }
    }
}