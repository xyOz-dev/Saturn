using System;
using System.Collections.Generic;

namespace Saturn.Agents.Core
{
    public class Mode
    {
        public Guid Id { get; set; }
        public string? Name { get; set; }
        public string? AgentName { get; set; }
        public string? Description { get; set; }
        public List<string> ToolNames { get; set; }
        public string? SystemPromptOverride { get; set; }
        public string Model { get; set; }
        public double Temperature { get; set; }
        public int MaxTokens { get; set; }
        public double TopP { get; set; }
        public double FrequencyPenalty { get; set; }
        public double PresencePenalty { get; set; }
        public bool EnableStreaming { get; set; }
        public bool MaintainHistory { get; set; }
        public bool RequireCommandApproval { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime ModifiedDate { get; set; }
        public bool IsDefault { get; set; }

        public Mode()
        {
            Id = Guid.NewGuid();
            ToolNames = new List<string>();
            CreatedDate = DateTime.UtcNow;
            ModifiedDate = DateTime.UtcNow;
            Temperature = 0.7;
            MaxTokens = 4096;
            TopP = 1.0;
            FrequencyPenalty = 0;
            PresencePenalty = 0;
            EnableStreaming = true;
            MaintainHistory = true;
            RequireCommandApproval = false;
            Model = "openai/gpt-4o";
        }

        public static Mode CreateDefault()
        {
            return new Mode
            {
                Id = Guid.Empty,
                Name = "Default",
                AgentName = "Assistant",
                Description = "Standard configuration with all tools enabled",
                IsDefault = true,
                ToolNames = new List<string>(),
                SystemPromptOverride = null,
                Model = "openai/gpt-4o",
                Temperature = 0.7,
                MaxTokens = 4096,
                TopP = 1.0,
                FrequencyPenalty = 0,
                PresencePenalty = 0,
                EnableStreaming = true,
                MaintainHistory = true,
                RequireCommandApproval = false
            };
        }

        public void ApplyToConfiguration(AgentConfiguration config)
        {
            config.Name = AgentName;
            config.Model = Model;
            config.Temperature = Temperature;
            config.MaxTokens = MaxTokens;
            config.TopP = TopP;
            config.FrequencyPenalty = FrequencyPenalty;
            config.PresencePenalty = PresencePenalty;
            config.EnableStreaming = EnableStreaming;
            config.MaintainHistory = MaintainHistory;
            config.RequireCommandApproval = RequireCommandApproval;
            
            if (!string.IsNullOrWhiteSpace(SystemPromptOverride))
            {
                config.SystemPrompt = SystemPromptOverride;
            }
            
            config.ToolNames = new List<string>(ToolNames);
            config.EnableTools = ToolNames.Count > 0;
        }

        public static Mode FromConfiguration(AgentConfiguration config, string modeName)
        {
            return new Mode
            {
                Name = modeName,
                AgentName = config.Name,
                ToolNames = new List<string>(config.ToolNames ?? new List<string>()),
                SystemPromptOverride = config.SystemPrompt,
                Model = config.Model,
                Temperature = config.Temperature ?? 0.7,
                MaxTokens = config.MaxTokens ?? 4096,
                TopP = config.TopP ?? 1.0,
                FrequencyPenalty = config.FrequencyPenalty ?? 0,
                PresencePenalty = config.PresencePenalty ?? 0,
                EnableStreaming = config.EnableStreaming,
                MaintainHistory = config.MaintainHistory,
                RequireCommandApproval = config.RequireCommandApproval
            };
        }

        public Mode Clone()
        {
            return new Mode
            {
                Id = Guid.NewGuid(),
                Name = $"{Name} (Copy)",
                AgentName = AgentName,
                Description = Description,
                ToolNames = new List<string>(ToolNames),
                SystemPromptOverride = SystemPromptOverride,
                Model = Model,
                Temperature = Temperature,
                MaxTokens = MaxTokens,
                TopP = TopP,
                FrequencyPenalty = FrequencyPenalty,
                PresencePenalty = PresencePenalty,
                EnableStreaming = EnableStreaming,
                MaintainHistory = MaintainHistory,
                RequireCommandApproval = RequireCommandApproval,
                CreatedDate = DateTime.UtcNow,
                ModifiedDate = DateTime.UtcNow,
                IsDefault = false
            };
        }
    }
}