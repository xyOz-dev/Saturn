using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Saturn.OpenRouter;
using Saturn.OpenRouter.Models.Api.Models;
using Saturn.Providers;

namespace Saturn.UI
{
    public class AgentConfiguration
    {
        public string Model { get; set; } = "anthropic/claude-sonnet-4";
        public double Temperature { get; set; } = 0.15;
        public int MaxTokens { get; set; } = 16096;
        public double TopP { get; set; } = 0.25;
        public bool EnableStreaming { get; set; } = true;
        public bool MaintainHistory { get; set; } = true;
        public int MaxHistoryMessages { get; set; } = 10;
        public string SystemPrompt { get; set; }
        public bool EnableTools { get; set; } = false;
        public List<string> ToolNames { get; set; } = new List<string>();
        public bool RequireCommandApproval { get; set; } = true;
        
        private static List<Model>? _cachedModels;
        private static DateTime _cacheTime;
        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);

        public AgentConfiguration()
        {
            SystemPrompt = @"You are a CLI based coding assistant. Your overall goal is to execute and complete the users task USING the provided tools.
Prime Directive
- Complete the user's task accurately and efficiently using the provided tools and the current project context.
- Favor minimal, targeted changes that preserve existing behavior unless a refactor is explicitly requested.
- Think through the task internally; share only a concise plan and the final results. Do not expose long chain-of-thought.

Operating Principles
1) Tool usage
   - Prefer tools over assumptions. Read before you write.
   - Choose the smallest-capability tool that can complete the step.
   - On errors, analyze the message, adjust, and retry with exponential backoff up to the retry limit.
   - Never fabricate tool results; if a tool is missing or insufficient, say so and propose alternatives.

2) Planning
   - Make a brief plan before edits or commands. Keep the plan to 3–7 bullets.
   - If requirements are ambiguous or risky, ask targeted clarifying questions before proceeding.
   - If uncertainty is minor, proceed with safe assumptions and state them.

3) File system awareness
   - Treat the provided tree as a snapshot; verify paths with a read/list tool before modifying.
   - Use relative paths from the project root. Respect case sensitivity and OS path rules.
   - Honor .gitignore and avoid build artifacts, vendor, and large binaries.
   - Preserve file formatting, headers, licenses, EOLs, and encoding.

4) Coding standards
   - Match the project's language, style, and tooling (formatter, linter, type checker). Look for config files (e.g., Prettier, ESLint, Black, mypy, flake8, isort, EditorConfig).
   - Write clear, maintainable code with focused comments for non-obvious logic. Include docstrings and types where customary.
   - Keep changes small and cohesive; avoid drive-by edits.

5) Testing and verification
   - Add or update tests when introducing behavior changes.
   - Run available test/build/type-check tools to validate changes.
   - Provide steps or commands for the user to verify locally.

6) Safety and privacy
   - Do not expose secrets, tokens, or credentials. Do not hardcode secrets.
   - Do not execute untrusted scripts or enable network access unless the policy explicitly allows it.
   - Confirm before destructive actions (deleting, overwriting, schema migrations, large refactors).

7) Performance and scalability
   - Prefer linear-time approaches and avoid unnecessary full-repo scans.
   - Stream or paginate large files; avoid loading huge blobs entirely.
   - Be mindful of dependency sizes and build times.";
        }

        public static async Task<List<Model>> GetAvailableModels(ILLMClient client)
        {
            if (_cachedModels != null && DateTime.UtcNow - _cacheTime < CacheDuration)
            {
                return _cachedModels;
            }

            try
            {
                var models = await client.GetModelsAsync();
                if (models != null && models.Any())
                {
                    _cachedModels = models
                        .Where(m => !string.IsNullOrEmpty(m.Id))
                        .Select(m => new Model { Id = m.Id, Name = m.Name })
                        .OrderBy(m => m.Name ?? m.Id)
                        .ToList();
                    _cacheTime = DateTime.UtcNow;
                    return _cachedModels;
                }
            }
            catch (Exception)
            {
            }

            return GetDefaultModels();
        }

        public static async Task<List<Model>> GetAvailableModels(OpenRouterClient client)
        {
            if (_cachedModels != null && DateTime.UtcNow - _cacheTime < CacheDuration)
            {
                return _cachedModels;
            }

            try
            {
                var response = await client.Models.ListAllAsync();
                if (response?.Data != null)
                {
                    _cachedModels = response.Data
                        .Where(m => !string.IsNullOrEmpty(m.Id))
                        .OrderBy(m => m.Name ?? m.Id)
                        .ToList();
                    _cacheTime = DateTime.UtcNow;
                    return _cachedModels;
                }
            }
            catch (Exception)
            {
            }

            return GetDefaultModels();
        }

        private static List<Model> GetDefaultModels()
        {
            var defaults = new[]
            {
                ("openai/gpt-5", "OpenAI: GPT-5"),
                ("openai/gpt-5-mini", "OpenAI: GPT-5-Mini"),
                ("openai/gpt-5-nano", "OpenAI: GPT-5-Nano"),
                ("openai/gpt-oss-120b", "OpenAI: GPT-OSS-120B"),
                ("openai/gpt-oss-20b", "OpenAI: GPT-OSS-20B"),
                ("anthropic/claude-opus-4.1", "Anthropic: Opus-4.1"),
                ("anthropic/claude-opus-4", "Anthropic: Opus-4"),
                ("anthropic/claude-sonnet-4", "Anthropic: Sonnet-4"),
                ("anthropic/claude-3.7-sonnet", "Anthropic: Sonnet-3.7"),
                ("anthropic/claude-3.5-haiku", "Anthropic: Haiku-3.5"),
                ("moonshotai/kimi-k2:free", "Kimi-K2")
            };

            return defaults.Select(d => new Model { Id = d.Item1, Name = d.Item2 }).ToList();
        }

        public AgentConfiguration Clone()
        {
            return new AgentConfiguration
            {
                Model = Model,
                Temperature = Temperature,
                MaxTokens = MaxTokens,
                TopP = TopP,
                EnableStreaming = EnableStreaming,
                MaintainHistory = MaintainHistory,
                MaxHistoryMessages = MaxHistoryMessages,
                SystemPrompt = SystemPrompt,
                EnableTools = EnableTools,
                ToolNames = new List<string>(ToolNames ?? new List<string>()),
                RequireCommandApproval = RequireCommandApproval
            };
        }
    }
}