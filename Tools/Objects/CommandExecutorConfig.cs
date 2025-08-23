using System;

namespace Saturn.Tools.Objects
{
    public class CommandExecutorConfig
    {
        public SecurityMode SecurityMode { get; set; } = SecurityMode.Unrestricted;
        public int DefaultTimeout { get; set; } = 30;
        public bool EnableHistory { get; set; } = true;
        public int MaxHistorySize { get; set; } = 100;
        public Func<string, CommandValidationResult>? CustomValidator { get; set; }
    }
}