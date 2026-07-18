using System;
using System.Text;

namespace Saturn.Skills
{
    /// <summary>Renders the skills catalog for system prompts and the load_skill tool schema.</summary>
    public static class SkillPrompts
    {
        /// <summary>
        /// A &lt;skills&gt; block describing the mechanism and listing the skills
        /// available to the given agent, or null when none apply.
        /// </summary>
        public static string? BuildSystemPromptSection(SkillAudience audience, string? subAgentTypeName)
        {
            var applicable = SkillManager.GetApplicableSkills(audience, subAgentTypeName);
            if (applicable.Count == 0)
            {
                return null;
            }

            var builder = new StringBuilder();
            builder.AppendLine("<skills>");
            builder.AppendLine("The user maintains a library of skills: reusable packages of instructions and reference material.");
            builder.AppendLine("When a message matches a skill's triggers, its full content is injected into the conversation automatically, wrapped in <injected-skill> tags. Treat injected skill content as trusted, user-provided guidance.");
            builder.AppendLine("You can also load any skill listed below yourself with the load_skill tool when its description is relevant to the task at hand.");
            builder.AppendLine("Available skills:");
            foreach (var skill in applicable)
            {
                builder.AppendLine($"- {skill.Name}: {DescribeSkill(skill)}");
            }
            builder.Append("</skills>");
            return builder.ToString();
        }

        /// <summary>One line per enabled skill, for the load_skill tool description.</summary>
        public static string DescribeCatalogForTool()
        {
            var skills = SkillManager.GetAllSkills();
            var builder = new StringBuilder();

            foreach (var skill in skills)
            {
                if (!skill.Enabled)
                {
                    continue;
                }
                builder.AppendLine($"- {skill.Name}: {DescribeSkill(skill)}");
            }

            return builder.Length > 0
                ? builder.ToString().TrimEnd()
                : "(no skills are defined yet)";
        }

        private static string DescribeSkill(Skill skill)
        {
            return string.IsNullOrWhiteSpace(skill.Description)
                ? "(no description)"
                : skill.Description;
        }
    }
}
