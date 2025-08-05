using OpenRouterSharp;
using OpenRouterSharp.Models.Requests;
using OpenRouterSharp.Models.Responses;
using Saturn.Agents;
using Saturn.Agents.Core;
using System;
using System.Threading.Tasks;

namespace Saturn
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            var apiKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
            var client = new OpenRouterClient(apiKey);

            var agentConfig = new AgentConfiguration
            {
                Name = "Code Assistant",
                SystemPrompt = "You are a software engineer, your goal is to help the user write, debug and evaluate code.",
                Client = client,
                Model = "openai/gpt-4.1",
                Temperature = 0.15,
                MaxTokens = 500,
                TopP = 0.25,
                MaintainHistory = true,
                MaxHistoryMessages = 10,
                EnableTools = true,
                ToolNames = new List<string>() { "apply_diff", "grep", "glob", "read_file" },
                
            };

            var agent = new DefaultAgent(agentConfig);

            while (true)
            {
                try
                {
                    Console.Write(">");
                    string input = Console.ReadLine();

                    Message response = await agent.Execute<Message>(input);

                    Console.WriteLine(response.Content.ToString());
                }
                catch
                {

                }
            }

            await Task.Delay(-1);
        }
    }
}
