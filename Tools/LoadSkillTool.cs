using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Saturn.Skills;
using Saturn.Tools.Core;

namespace Saturn.Tools
{
    public class LoadSkillTool : ToolBase
    {
        public override string Name => "load_skill";

        // The catalog is rendered live from the skill store on every request, so
        // skills created or edited mid-session appear here without a restart.
        public override string Description =>
            @"Load a skill from the user's skill library into the conversation. Skills are reusable packages of instructions and reference material from the user's global library or the project's .saturn/skills directory; each loaded skill states its provenance.

Use this when a listed skill's description is relevant to the current task and its content has not already been injected. Skills whose triggers match the user's message are injected automatically; you only need this tool for skills you want proactively. Some skills are restricted to specific agents and are rejected when loaded from the wrong one.

Available skills:
" + SkillPrompts.DescribeCatalogForTool();

        protected override Dictionary<string, object> GetParameterProperties()
        {
            return new Dictionary<string, object>
            {
                ["name"] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["description"] = "The name of the skill to load, exactly as listed in the available skills"
                }
            };
        }

        protected override string[] GetRequiredParameters()
        {
            return new[] { "name" };
        }

        public override string GetDisplaySummary(Dictionary<string, object> parameters)
        {
            var name = GetParameter<string>(parameters, "name", "skill");
            return $"Loading skill: {name}";
        }

        public override Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters)
        {
            var name = GetParameter<string>(parameters, "name", string.Empty)?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                return Task.FromResult(CreateErrorResult("Parameter 'name' cannot be empty"));
            }

            // Fail closed: without a known audience there is no basis to grant
            // access, and audience None means this agent gets no skills at all.
            var context = AgentContext.Current;
            var configuration = context?.Configuration;
            if (configuration == null || !configuration.EnableSkills || configuration.SkillAudience == SkillAudience.None)
            {
                return Task.FromResult(CreateErrorResult("Skills are not enabled for this agent"));
            }

            var skill = SkillManager.GetSkillByName(name);
            if (skill == null || !skill.Enabled)
            {
                return Task.FromResult(CreateErrorResult(
                    $"No skill named '{name}' is available. Available skills:\n{SkillPrompts.DescribeCatalogForTool()}"));
            }

            if (!skill.AppliesTo(configuration.SkillAudience, configuration.SubAgentTypeName))
            {
                return Task.FromResult(CreateErrorResult(
                    $"Skill '{skill.Name}' is not available to this agent"));
            }

            var history = context?.Agent?.ChatHistory;
            if (history != null && SkillEnvelope.FindInjectedSkillNames(history).Contains(skill.Name))
            {
                return Task.FromResult(CreateSuccessResult(
                    new Dictionary<string, object>
                    {
                        ["name"] = skill.Name,
                        ["status"] = "already_loaded"
                    },
                    $"Skill '{skill.Name}' is already loaded in this conversation; refer to its earlier <injected-skill> content."));
            }

            var envelope = SkillEnvelope.Build(skill, requestedByModel: true);
            return Task.FromResult(CreateSuccessResult(
                new Dictionary<string, object>
                {
                    ["name"] = skill.Name,
                    ["status"] = "loaded"
                },
                envelope));
        }
    }
}
