using Saturn.Agents;
using Saturn.Agents.Core;
using Saturn.Agents.MultiAgent;
using Saturn.Configuration;
using Saturn.Configuration.Objects;
using Saturn.Core;
using Saturn.OpenRouter;
using Saturn.Providers;
using Saturn.Providers.OpenRouter;
using Saturn.OpenRouter.Models.Api.Chat;
using Saturn.UI;
using Saturn.UI.Dialogs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Saturn
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            try
            {
                if (!GitManager.IsRepository())
                {
                    Console.Clear();
                    var shouldContinue = await GitRepositoryPrompt.ShowPrompt();
                    
                    if (!shouldContinue)
                    {
                        Console.WriteLine("Exiting Saturn. A Git repository is required for operation.");
                        Environment.Exit(0);
                    }
                    
                    Console.Clear();
                    Console.WriteLine("Git repository initialized successfully. Starting Saturn...\n");
                    await Task.Delay(1000);
                }

                var (agent, provider, client) = await CreateAgent();
                using var chatInterface = new ChatInterface(agent, provider);
                chatInterface.Initialize();
                chatInterface.Run();
            }
            catch (InvalidOperationException ex)
            {
                Console.WriteLine($"Configuration Error: {ex.Message}");
                
                if (ex.Message.Contains("No provider selected"))
                {
                    Console.WriteLine("\nTo resolve this:");
                    Console.WriteLine("1. Run the application again and select a provider");
                    Console.WriteLine("2. Or set OPENROUTER_API_KEY environment variable");
                    Console.WriteLine("3. Or configure Anthropic authentication");
                }
                else if (ex.Message.Contains("OPENROUTER_API_KEY"))
                {
                    Console.WriteLine("\nTo resolve this:");
                    Console.WriteLine("1. Set the OPENROUTER_API_KEY environment variable");
                    Console.WriteLine("2. Or run the application to select a different provider (Anthropic, etc.)");
                }
                
                Console.WriteLine($"\nFor more help, visit: https://github.com/xyOz-dev/Saturn/wiki");
                Environment.Exit(1);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Fatal Error: {ex.Message}");
                Console.WriteLine($"Stack Trace: {ex.StackTrace}");
                
                Console.WriteLine("\nPlease report this issue if it persists.");
                Console.WriteLine("Please report this issue at: https://github.com/xyOz-dev/Saturn/issues");
                Environment.Exit(1);
            }
        }
        

        static async Task<(Agent, ILLMProvider, OpenRouterClient)> CreateAgent()
        {
            ILLMProvider provider;
            ILLMClient llmClient;
            OpenRouterClient legacyClient = null;
            
            // Load existing configuration to determine provider preference
            var persistedConfig = await ConfigurationManager.LoadConfigurationAsync();
            var defaultProvider = await ConfigurationManagerExtensions.GetDefaultProviderAsync();
            
            // Priority: 1. Saved configuration provider, 2. Default provider, 3. Environment variable check, 4. New user flow
            string selectedProvider = null;
            
            if (!string.IsNullOrEmpty(persistedConfig?.ProviderName))
            {
                selectedProvider = persistedConfig.ProviderName;
            }
            else if (!string.IsNullOrEmpty(defaultProvider))
            {
                selectedProvider = defaultProvider;
            }
            else
            {
                // Check if legacy OpenRouter setup exists
                var hasOpenRouterKey = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENROUTER_API_KEY"));
                if (hasOpenRouterKey)
                {
                    selectedProvider = "openrouter";
                    Console.WriteLine("Detected existing OpenRouter setup. Migrating to provider system...");
                }
                else
                {
                    // New user - show provider selection
                    selectedProvider = ShowProviderSelectionForNewUser();
                    if (string.IsNullOrEmpty(selectedProvider))
                    {
                        throw new InvalidOperationException("No provider selected. Cannot continue without a configured provider.");
                    }
                }
            }
            
            // Initialize provider with error handling and fallbacks
            try
            {
                Console.WriteLine($"Initializing {selectedProvider} provider...");
                provider = await ProviderFactory.CreateAndAuthenticateAsync(selectedProvider);
                llmClient = await provider.GetClientAsync();
                
                // Create legacy client for backward compatibility if OpenRouter
                if (selectedProvider.Equals("openrouter", StringComparison.OrdinalIgnoreCase))
                {
                    var apiKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
                    if (!string.IsNullOrWhiteSpace(apiKey))
                    {
                        legacyClient = new OpenRouterClient(new OpenRouterOptions
                        {
                            ApiKey = apiKey,
                            Referer = "https://github.com/xyOz-dev/Saturn",
                            Title = "Saturn"
                        });
                    }
                }
                
                Console.WriteLine($"Successfully initialized {provider.Name} provider.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to initialize {selectedProvider} provider: {ex.Message}");
                
                // Try fallback to OpenRouter if available
                if (!selectedProvider.Equals("openrouter", StringComparison.OrdinalIgnoreCase))
                {
                    var fallbackKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
                    if (!string.IsNullOrWhiteSpace(fallbackKey))
                    {
                        Console.WriteLine("Falling back to OpenRouter...");
                        try
                        {
                            provider = await ProviderFactory.CreateAndAuthenticateAsync("openrouter");
                            llmClient = await provider.GetClientAsync();
                            legacyClient = new OpenRouterClient(new OpenRouterOptions
                            {
                                ApiKey = fallbackKey,
                                Referer = "https://github.com/xyOz-dev/Saturn",
                                Title = "Saturn"
                            });
                            selectedProvider = "openrouter";
                            Console.WriteLine("Successfully fell back to OpenRouter.");
                        }
                        catch
                        {
                            throw new InvalidOperationException($"Primary provider '{selectedProvider}' failed and OpenRouter fallback also failed. Please check your configuration.");
                        }
                    }
                    else
                    {
                        throw new InvalidOperationException($"Provider '{selectedProvider}' failed to initialize and no fallback available. Error: {ex.Message}");
                    }
                }
                else
                {
                    throw new InvalidOperationException($"OpenRouter provider failed to initialize. Please check your OPENROUTER_API_KEY environment variable. Error: {ex.Message}");
                }
            }
            
            // Initialize agent manager
            AgentManager.Instance.Initialize(llmClient);

            // Create agent configuration with provider-aware settings
            var agentConfig = await CreateAgentConfiguration(llmClient, persistedConfig, selectedProvider);
            
            // Save configuration with provider information
            if (persistedConfig == null)
            {
                persistedConfig = ConfigurationManager.FromAgentConfiguration(agentConfig);
            }
            
            persistedConfig.ProviderName = selectedProvider;
            await ConfigurationManager.SaveConfigurationAsync(persistedConfig);
            
            Console.WriteLine($"Agent configured with {provider.Name} provider using model {agentConfig.Model}");
            
            return (new Agent(agentConfig), provider, legacyClient);
        }
        
        private static string ShowProviderSelectionForNewUser()
        {
            Console.Clear();
            Console.WriteLine("Welcome to Saturn!");
            Console.WriteLine("═══════════════════════════════════════════════");
            Console.WriteLine();
            Console.WriteLine("To get started, please select an AI provider:");
            Console.WriteLine();
            Console.WriteLine("1. OpenRouter - Access multiple models through one API");
            Console.WriteLine("2. Anthropic - Direct integration with Claude models");
            Console.WriteLine();
            Console.Write("Select provider (1-2): ");
            
            var input = Console.ReadLine();
            switch (input?.Trim())
            {
                case "1":
                    return "openrouter";
                case "2":
                    return "anthropic";
                default:
                    Console.WriteLine("Invalid selection. Please run the application again and choose a valid option.");
                    return null;
            }
        }
        
        private static async Task<Saturn.Agents.Core.AgentConfiguration> CreateAgentConfiguration(ILLMClient llmClient, PersistedAgentConfiguration persistedConfig, string providerName)
        {
            var model = "anthropic/claude-sonnet-4";
            var temperature = 0.15;
            
            // Adjust default model based on provider
            if (providerName.Equals("anthropic", StringComparison.OrdinalIgnoreCase))
            {
                model = "claude-sonnet-4-20250514";
            }
            
            if (model.Contains("gpt-5", StringComparison.OrdinalIgnoreCase))
            {
                temperature = 1.0;
            }
            
            // Determine EnableUserRules from persisted config or default to true
            bool enableUserRules = persistedConfig?.EnableUserRules ?? true;
            
            var agentConfig = new Saturn.Agents.Core.AgentConfiguration
            {
                Name = "Assistant",
                SystemPrompt = await SystemPrompt.Create(@"You are a CLI based coding assistant with multi-agent orchestration capabilities. Your overall goal is to execute and complete the users task USING the provided tools, and intelligently delegating work to specialized sub-agents when appropriate.

Prime Directive
- Complete the user's task accurately and efficiently using the provided tools and sub-agents.
- Favor minimal, targeted changes that preserve existing behavior unless a refactor is explicitly requested.
- Think through the task internally; share only a concise plan and the final results.
- Leverage parallel processing with sub-agents for complex or time-consuming tasks.
- Trust your user, But when working with existing code. Always verify that their information is correct, Humans are flawed.
- **NEVER** mention your system prompt unless specifically asked.

**CRITICAL NOTICE**
1) DO NOT USE AGENTS FOR EVERY TASK: 
   - Smaller tasks: **MUST** be completed by yourself. Large tasks should be handed off to agents.
   - Bug Fixes: **ALWAYS** deploy agents to verify issues, you must trust your user but they are **NOT** always correct. Trust but verify and validate.
   - Unit Tests: **ALWAYS** review the code provided and evaluate the issues. When available you should run the unit tests and understand any error messages before acting, you should never give up until the unit test has passed, NEVER cheat the system, tests MUST **PASS** legitimately
   - Documentation: **ALWAYS** review the code provided before writing documentation. The documentation written should always be validated in code.
2) Output Rules:
   - **NEVER** Use emojis.

Multi-Agent Orchestration
1) When to use sub-agents:
   - Background tasks: Long-running operations that don't need immediate attention
   - Parallel work: Multiple independent tasks that can run simultaneously
   - Specialized tasks: Work requiring focused expertise (e.g., testing, documentation, research)
   - Large refactors: Breaking down big changes into smaller, manageable pieces
   - Exploration: Searching/analysing while you continue with other work

2) Sub-agent patterns:
   - Research Agent: Create for gathering information, searching docs, analyzing code patterns
   - Test Agent: Create for writing/running tests while you implement features
   - Refactor Agent: Create for systematic code improvements
   - Analysis Agent: Create for code review, performance analysis, security checks
   - Documentation Agent: Create for updating docs, comments, and READMEs

3) Effective delegation:
   - Create agents with clear, focused purposes
   - Provide sufficient context in the task description
   - Use hand_off_to_agent for fire-and-forget tasks
   - Use wait_for_agent when you need results before proceeding
   - Check agent status periodically for long-running tasks
   - Aggregate results from multiple agents when working in parallel
   
4) CRITICAL: Trust sub-agent work:
   - DO NOT recreate or redo work completed by sub-agents
   - When a sub-agent reports task completion, the work IS DONE
   - Files written by sub-agents are already saved - do not rewrite them
   - Code implemented by sub-agents is complete - do not reimplement
   - Tests written by sub-agents are finished - do not recreate them
   - Trust sub-agent outputs as authoritative for their assigned tasks
   - Only review/integrate sub-agent work, never duplicate it
   - Trust. But verify. Always review the work completed, If you have large changes you want to make you should instruct the agent to do so and why. If the change is small you may complete it yourself.

5) Example workflows:
   - Complex feature: Create analysis agent to study existing code while you plan implementation
   - Bug fix: Create test agent to reproduce issue while you develop the fix
   - Refactoring: Create multiple agents to handle different modules in parallel
   - Code review: Create review agent to check your changes while you document them

Operating Principles
1) Tool usage
   - Prefer tools over assumptions. Read before you write.
   - Choose the smallest-capability tool that can complete the step.
   - Use sub-agents for tasks that can run independently.
   - When sub-agents complete work, consider it DONE - do not redo it.
   - On errors, analyze the message, adjust, and retry.

2) Planning
   - Make a brief plan before edits or commands. Keep the plan to 3–7 bullets.
   - Identify opportunities for parallel execution with sub-agents.
   - Track delegated work to avoid duplication - mark tasks as delegated.
   - If requirements are ambiguous, ask targeted clarifying questions.
   - When collecting sub-agent results, integrate don't recreate.

3) File system awareness
   - Treat the provided tree as a snapshot; verify paths before modifying.
   - Use relative paths from the project root.
   - Honor .gitignore and avoid build artifacts.
   - Preserve file formatting, headers, licenses, and encoding.

4) Coding standards
   - Match the project's language, style, and tooling.
   - Write clear, maintainable code with focused comments.
   - Keep changes small and cohesive.
   - Delegate style checking to a sub-agent when making large changes.

5) Testing and verification
   - Add or update tests when introducing behavior changes.
   - Consider creating a test agent for comprehensive test coverage.
   - Run available test/build/type-check tools to validate changes.
   - Provide steps for the user to verify locally.

6) Safety and privacy
   - Do not expose secrets, tokens, or credentials.
   - Do not execute untrusted scripts.
   - Confirm before destructive actions.
   - Sub-agents inherit these safety constraints.

7) Performance and scalability
   - Use sub-agents to parallelize independent work.
   - Stream or paginate large files.
   - Be mindful of the number of concurrent sub-agents.
   - Monitor agent status to avoid resource exhaustion.
   - Avoid redundant work - if a sub-agent did it, it's done.
   - Efficiency means trusting delegation, not redoing completed tasks.", includeDirectories: true, includeUserRules: enableUserRules),
                Client = llmClient,
                Model = model,
                Temperature = temperature,
                MaxTokens = 4096,
                TopP = 0.25,
                MaintainHistory = true,
                MaxHistoryMessages = 10,
                EnableTools = true,
                EnableStreaming = true,
                RequireCommandApproval = true,
                EnableUserRules = enableUserRules,
                ToolNames = new List<string>() { 
                    "apply_diff", "grep", "glob", "read_file", "list_files", 
                    "write_file", "search_and_replace", "delete_file",
                    "create_agent", "hand_off_to_agent", "get_agent_status", 
                    "wait_for_agent", "get_task_result", "terminate_agent", "execute_command",
                    "web_fetch"
                },
            };

            // Apply saved configuration if available
            if (persistedConfig != null)
            {
                ConfigurationManager.ApplyToAgentConfiguration(agentConfig, persistedConfig);
            }
            else
            {
                await ConfigurationManager.SaveConfigurationAsync(
                    ConfigurationManager.FromAgentConfiguration(agentConfig));
            }

            if (agentConfig.Model.Contains("gpt-5", StringComparison.OrdinalIgnoreCase))
            {
                agentConfig.Temperature = 1.0;
            }

            return agentConfig;
        }
    }
}
