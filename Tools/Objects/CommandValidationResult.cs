namespace Saturn.Tools.Objects
{
    public class CommandValidationResult
    {
        public bool IsValid { get; set; }
        public string Reason { get; set; } = string.Empty;
    }
}