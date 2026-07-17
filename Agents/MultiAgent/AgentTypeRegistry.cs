using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Saturn.Agents.MultiAgent
{
    public sealed class AgentTypeDefinition
    {
        public string Name { get; init; } = "";
        public string Description { get; init; } = "";

        /// <summary>
        /// Tools this type may use; null grants every tool available to sub-agents.
        /// Orchestration tools are excluded for all types regardless of this list.
        /// </summary>
        public IReadOnlyList<string>? ToolNames { get; init; }

        /// <summary>Appended to the standard sub-agent system prompt.</summary>
        public string? SystemPromptAddendum { get; init; }
    }

    /// <summary>
    /// Built-in sub-agent types. The spawn_agent tool schema and the orchestrator
    /// system prompt are both rendered from this registry so they cannot drift.
    /// </summary>
    public static class AgentTypeRegistry
    {
        public const string DefaultTypeName = "general";

        public static readonly IReadOnlyList<AgentTypeDefinition> All = new[]
        {
            new AgentTypeDefinition
            {
                Name = "general",
                Description = "Full capabilities. Use when the task mixes reading and modifying, or does not fit a more specific type.",
            },
            new AgentTypeDefinition
            {
                Name = "explorer",
                Description = "Read-only exploration: locate code, trace behavior, summarize findings across many files. Cannot modify files or run commands.",
                ToolNames = new[] { "grep", "glob", "read_file", "list_files", "web_fetch", "web_search" },
                SystemPromptAddendum = "You are read-only: report findings, never attempt to change anything. Cite file paths and line numbers in your report so the orchestrator can act on them directly.",
            },
            new AgentTypeDefinition
            {
                Name = "coder",
                Description = "Implementation work: write or modify code, run builds and tests to verify.",
                SystemPromptAddendum = "Match the project's existing style, naming, and comment density. Verify your changes by building or running tests when possible, and state in your report exactly what you verified and how.",
            },
            new AgentTypeDefinition
            {
                Name = "reviewer",
                Description = "Code review and verification: read code, run tests and checks, report issues. Does not modify files.",
                ToolNames = new[] { "grep", "glob", "read_file", "list_files", "execute_command", "get_command_output", "kill_command", "web_fetch", "web_search" },
                SystemPromptAddendum = "Do not modify any files. Review the code or changes you are pointed at, running tests where useful. Report concrete findings with file paths and line numbers, ordered by severity, and say explicitly if you found no issues.",
            },
        };

        public static bool TryGet(string? name, out AgentTypeDefinition definition)
        {
            definition = All.FirstOrDefault(t =>
                t.Name.Equals(name, StringComparison.OrdinalIgnoreCase))!;
            return definition != null;
        }

        public static AgentTypeDefinition Default =>
            All.First(t => t.Name == DefaultTypeName);

        public static string[] Names => All.Select(t => t.Name).ToArray();

        /// <summary>One line per type, for embedding in prompts and tool schemas.</summary>
        public static string DescribeAll(string indent = "")
        {
            var builder = new StringBuilder();
            foreach (var type in All)
            {
                builder.AppendLine($"{indent}- {type.Name}: {type.Description}");
            }
            return builder.ToString().TrimEnd();
        }
    }
}
