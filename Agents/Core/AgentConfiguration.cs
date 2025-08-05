using OpenRouterSharp;
using System.Collections.Generic;

namespace Saturn.Agents.Core
{
    public class AgentConfiguration
    {
        public required string Name { get; set; }
        public required string SystemPrompt { get; set; }
        public required OpenRouterClient Client { get; set; }
        public string Model { get; set; } = "gpt-4.1";
        public double? Temperature { get; set; }
        public int? MaxTokens { get; set; }
        public double? TopP { get; set; }
        public double? FrequencyPenalty { get; set; }
        public double? PresencePenalty { get; set; }
        public string[]? StopSequences { get; set; }
        public int? MaxHistoryMessages { get; set; } = 20;
        public bool MaintainHistory { get; set; } = true;
        public List<string> ToolNames { get; set; } = new List<string>();
        public bool EnableTools { get; set; } = false;
    }
}