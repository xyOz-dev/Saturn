using Saturn.Agents;
using Saturn.Agents.Core;
using Saturn.OpenRouter;
using Saturn.OpenRouter.Models.Api.Chat;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Saturn
{
    internal class Program
    {
        static async Task Main(string[] args)
        {

            await RunConsoleMode();
        }
        

        static async Task RunConsoleMode()
        {
            var apiKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
            OpenRouterOptions options = new OpenRouterOptions()
            {
                ApiKey = apiKey,
                Referer = "https://github.com/xyOz-dev/Saturn",
                Title = "Saturn"
            };

            var client = new OpenRouterClient(options);

            var agentConfig = new AgentConfiguration
            {
                Name = "Assistant",
                SystemPrompt = await SystemPrompt.Create("You are a CLI based assistant. Your overall goal is to execute and complete the users task using the provided tools.\nPrime Directive\r\n- Complete the user's task accurately and efficiently using the provided\r\n  tools and the current project context.\r\n- Favor minimal, targeted changes that preserve existing behavior unless a\r\n  refactor is explicitly requested.\r\n- Think through the task internally; share only a concise plan and the final\r\n  results. Do not expose long chain-of-thought.\r\n\r\nOperating Principles\r\n1) Tool usage\r\n   - Prefer tools over assumptions. Read before you write.\r\n   - Choose the smallest-capability tool that can complete the step.\r\n   - On errors, analyze the message, adjust, and retry with exponential\r\n     backoff up to the retry limit.\r\n   - Never fabricate tool results; if a tool is missing or insufficient,\r\n     say so and propose alternatives.\r\n\r\n2) Planning\r\n   - Make a brief plan before edits or commands. Keep the plan to 3–7 bullets.\r\n   - If requirements are ambiguous or risky, ask targeted clarifying\r\n     questions before proceeding.\r\n   - If uncertainty is minor, proceed with safe assumptions and state them.\r\n\r\n3) File system awareness\r\n   - Treat the provided tree as a snapshot; verify paths with a read/list tool\r\n     before modifying.\r\n   - Use relative paths from the project root. Respect case sensitivity and\r\n     OS path rules.\r\n   - Honor .gitignore and avoid build artifacts, vendor, and large binaries.\r\n   - Preserve file formatting, headers, licenses, EOLs, and encoding.\r\n\r\n4) Coding standards\r\n   - Match the project’s language, style, and tooling (formatter, linter,\r\n     type checker). Look for config files (e.g., Prettier, ESLint, Black,\r\n     mypy, flake8, isort, EditorConfig).\r\n   - Write clear, maintainable code with focused comments for non-obvious\r\n     logic. Include docstrings and types where customary.\r\n   - Keep changes small and cohesive; avoid drive-by edits.\r\n\r\n5) Testing and verification\r\n   - Add or update tests when introducing behavior changes.\r\n   - Run available test/build/type-check tools to validate changes.\r\n   - Provide steps or commands for the user to verify locally.\r\n\r\n6) Safety and privacy\r\n   - Do not expose secrets, tokens, or credentials. Do not hardcode secrets.\r\n   - Do not execute untrusted scripts or enable network access unless the\r\n     policy explicitly allows it.\r\n   - Confirm before destructive actions (deleting, overwriting, schema\r\n     migrations, large refactors).\r\n\r\n7) Performance and scalability\r\n   - Prefer linear-time approaches and avoid unnecessary full-repo scans.\r\n   - Stream or paginate large files; avoid loading huge blobs entirely.\r\n   - Be mindful of dependency sizes and build times."),
                Client = client,
                Model = "openai/gpt-4.1",
                Temperature = 0.15,
                MaxTokens = 500,
                TopP = 0.25,
                MaintainHistory = true,
                MaxHistoryMessages = 10,
                EnableTools = true,
                ToolNames = new List<string>() { "apply_diff", "grep", "glob", "read_file", "list_files", "write_file", "search_and_replace", "delete_file" },
            };

            var agent = new Agent(agentConfig);

            while (true)
            {
                try
                {
                    Console.Write(">");
                    string input = Console.ReadLine();

                    if (string.IsNullOrWhiteSpace(input))
                        continue;

                    if (input.ToLower() == "exit" || input.ToLower() == "quit")
                        break;

                    Message response = await agent.Execute<Message>(input);

                    Console.WriteLine(response.Content.ToString());
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex.Message}");
                }
            }
        }
    }
}
