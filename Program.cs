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
                Name = "Code Assistant",
                SystemPrompt = "Your primary goal is to COMPLETE the users task using the provided tools.",
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
