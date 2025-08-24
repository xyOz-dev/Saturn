using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Saturn.Config.Objects
{
    public class SubAgentConfiguration
    {
        public string Model { get; set; } = "anthropic/claude-3.5-sonnet";
        public double Temperature { get; set; } = 0.3;
        public int MaxTokens { get; set; } = 4096;
        public double TopP { get; set; } = 0.95;
        public bool EnableTools { get; set; } = true;
        public string? SystemPromptOverride { get; set; }
    }
}
