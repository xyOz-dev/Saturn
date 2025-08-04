using OpenRouterSharp;
using OpenRouterSharp.Models.Requests;
using Saturn.Agents;
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
                Model = "openrouter/horizon-beta",
                Temperature = 0.15,
                MaxTokens = 500,
                TopP = 0.25,
                MaintainHistory = true,
                MaxHistoryMessages = 10
            };

            var agent = new DefaultAgent(agentConfig);

        }
    }
}
