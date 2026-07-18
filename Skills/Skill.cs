using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Saturn.Skills
{
    public enum SkillScope
    {
        Global,
        Workspace
    }

    /// <summary>Which agents a skill can be injected into.</summary>
    public enum SkillAudience
    {
        None,
        Orchestrator,
        SubAgent
    }

    public class Skill
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public List<string> Triggers { get; set; }
        public string Content { get; set; }
        public bool Enabled { get; set; }
        public bool ApplyToOrchestrator { get; set; }
        public bool ApplyToSubAgents { get; set; }

        /// <summary>
        /// Sub-agent types (AgentTypeRegistry names) this skill applies to;
        /// null or empty means every sub-agent type.
        /// </summary>
        public List<string>? SubAgentTypes { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        /// <summary>Where the skill file lives; derived from its location on load.</summary>
        [JsonIgnore]
        public SkillScope Scope { get; set; }

        public Skill()
        {
            Id = Guid.NewGuid().ToString("N");
            Name = string.Empty;
            Description = string.Empty;
            Triggers = new List<string>();
            Content = string.Empty;
            Enabled = true;
            ApplyToOrchestrator = true;
            ApplyToSubAgents = true;
            CreatedAt = DateTime.UtcNow;
            UpdatedAt = DateTime.UtcNow;
        }

        public bool AppliesTo(SkillAudience audience, string? subAgentTypeName)
        {
            if (!Enabled || audience == SkillAudience.None)
            {
                return false;
            }

            if (audience == SkillAudience.Orchestrator)
            {
                return ApplyToOrchestrator;
            }

            if (!ApplyToSubAgents)
            {
                return false;
            }

            if (SubAgentTypes == null || SubAgentTypes.Count == 0)
            {
                return true;
            }

            return subAgentTypeName != null && SubAgentTypes.Contains(subAgentTypeName, StringComparer.OrdinalIgnoreCase);
        }

        public Skill Clone()
        {
            return new Skill
            {
                Id = Id,
                Name = Name,
                Description = Description,
                Triggers = new List<string>(Triggers ?? new List<string>()),
                Content = Content,
                Enabled = Enabled,
                ApplyToOrchestrator = ApplyToOrchestrator,
                ApplyToSubAgents = ApplyToSubAgents,
                SubAgentTypes = SubAgentTypes == null ? null : new List<string>(SubAgentTypes),
                CreatedAt = CreatedAt,
                UpdatedAt = UpdatedAt,
                Scope = Scope
            };
        }
    }
}
