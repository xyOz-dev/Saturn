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
            Model = "anthropic/claude-sonnet-4";
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
                ToolNames = new List<string>() { 
                    "apply_diff", "grep", "glob", "read_file", "list_files", 
                    "write_file", "search_and_replace", "delete_file",
                    "create_agent", "hand_off_to_agent", "get_agent_status", 
                    "wait_for_agent", "get_task_result", "terminate_agent", "execute_command",
                    "web_fetch"
                },
                SystemPromptOverride = null,
                Model = "anthropic/claude-sonnet-4",
                Temperature = 0.15,
                MaxTokens = 4096,
                TopP = 0.25,
                FrequencyPenalty = 0,
                PresencePenalty = 0,
                EnableStreaming = true,
                MaintainHistory = true,
                RequireCommandApproval = true
            };
        }

        public void ApplyToConfiguration(AgentConfiguration config)
        {
            // Only update config.Name if AgentName is not null/whitespace
            if (!string.IsNullOrWhiteSpace(AgentName))
            {
                config.Name = AgentName;
            }
            
            config.Model = Model;
            config.Temperature = Temperature;
            config.MaxTokens = MaxTokens;
            config.TopP = TopP;
            config.FrequencyPenalty = FrequencyPenalty;
            config.PresencePenalty = PresencePenalty;
            config.EnableStreaming = EnableStreaming;
            config.MaintainHistory = MaintainHistory;
            config.RequireCommandApproval = RequireCommandApproval;
            
            // Only override system prompt if provided
            if (!string.IsNullOrWhiteSpace(SystemPromptOverride))
            {
                config.SystemPrompt = SystemPromptOverride;
            }
            
            // Defensively handle ToolNames
            if (ToolNames != null)
            {
                config.ToolNames = new List<string>(ToolNames);
                config.EnableTools = ToolNames.Count > 0;
            }
            // If ToolNames is null, leave config.ToolNames unchanged
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