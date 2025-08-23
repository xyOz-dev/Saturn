using System;

namespace Saturn.Tools.Objects
{
    public class CommandHistory
    {
        public string Command { get; set; } = string.Empty;
        public string WorkingDirectory { get; set; } = string.Empty;
        public DateTime ExecutedAt { get; set; }
        public int? ExitCode { get; set; }
        public TimeSpan? Duration { get; set; }
        public bool Success { get; set; }
        public string? Error { get; set; }
    }
}