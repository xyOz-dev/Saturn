using System;
using System.Collections.Generic;
using Saturn.OpenRouter;

namespace Saturn.Agents.Core
{
    public class AgentConfiguration
    {
        public required string Name { get; set; }
        public required string SystemPrompt { get; set; }
        public required OpenRouterClient Client { get; set; }
        public string Model { get; set; } = "anthropic/claude-sonnet-4";
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
        public bool EnableStreaming { get; set; } = false;
        public int StreamBufferSize { get; set; } = 1024;
        public bool RequireCommandApproval { get; set; } = true;
        public Guid? CurrentModeId { get; set; }
        
        public static AgentConfiguration FromMode(Mode mode, OpenRouterClient client)
        {
            string systemPrompt;
            if (!string.IsNullOrWhiteSpace(mode.SystemPromptOverride))
            {
                systemPrompt = mode.SystemPromptOverride;
            }
            else
            {
                systemPrompt = "You are a helpful assistant.";
            }
            
            var config = new AgentConfiguration
            {
                Name = mode.AgentName,
                SystemPrompt = systemPrompt,
                Client = client,
                Model = mode.Model,
                Temperature = mode.Temperature,
                MaxTokens = mode.MaxTokens,
                TopP = mode.TopP,
                FrequencyPenalty = mode.FrequencyPenalty,
                PresencePenalty = mode.PresencePenalty,
                EnableStreaming = mode.EnableStreaming,
                MaintainHistory = mode.MaintainHistory,
                RequireCommandApproval = mode.RequireCommandApproval,
                ToolNames = new List<string>(mode.ToolNames ?? new List<string>()),
                EnableTools = mode.ToolNames?.Count > 0,
                CurrentModeId = mode.Id
            };
            
            return config;
        }
    }
}