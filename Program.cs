using OpenRouterSharp;
using OpenRouterSharp.Models.Requests;
using OpenRouterSharp.Models.Responses;
using Saturn.Agents;
using Saturn.Agents.Core;
using Saturn.Web;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Saturn
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            var webMode = args.Contains("--web") || args.Contains("-w");
            var port = 8080;
            
            var portArg = args.FirstOrDefault(a => a.StartsWith("--port="));
            if (portArg != null && int.TryParse(portArg.Substring(7), out var customPort))
            {
                port = customPort;
            }

            if (webMode)
            {
                RunWebMode(port);
            }
            else
            {
                await RunConsoleMode();
            }
        }

        static void RunWebMode(int port)
        {
            var server = new HttpServer(port);
            
            try
            {
                server.Start();
                Console.WriteLine("Press 'q' to quit the web server...");
                
                while (true)
                {
                    var key = Console.ReadKey(true);
                    if (key.KeyChar == 'q' || key.KeyChar == 'Q')
                    {
                        break;
                    }
                }
                
                server.Stop();
                Console.WriteLine("Server stopped.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to start web server: {ex.Message}");
            }
        }

        static async Task RunConsoleMode()
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
                ToolNames = new List<string>() { "apply_diff", "grep", "glob", "read_file", "list_files" },
            };

            var agent = new DefaultAgent(agentConfig);

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
