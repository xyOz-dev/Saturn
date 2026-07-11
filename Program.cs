using Saturn.Agents;
using Saturn.Agents.Core;
using Saturn.Agents.MultiAgent;
using Saturn.Configuration;
using Saturn.Core;
using Saturn.Providers;
using Saturn.UI;
using System;
using System.Collections.Generic;
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

                var (agent, client) = await CreateAgent();
                using var chatInterface = new ChatInterface(agent, client);
                chatInterface.Initialize();
                chatInterface.Run();
            }
            catch (InvalidOperationException ex)
            {
                Console.WriteLine($"Configuration Error: {ex.Message}");
                Environment.Exit(1);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Fatal Error: {ex.Message}");
                Console.WriteLine($"Stack Trace: {ex.StackTrace}");
                Environment.Exit(1);
            }
        }
        

        static async Task<(Agent, ILlmClientSource)> CreateAgent()
        {
            ProviderRegistry.Register(new OpenRouterProvider());
            ProviderRegistry.Register(new LMStudioProvider());

            var persistedConfig = await ConfigurationManager.LoadConfigurationAsync();

            // Provider resolution: environment beats saved config beats the historical default.
            var providerName = Environment.GetEnvironmentVariable("SATURN_PROVIDER");
            if (string.IsNullOrWhiteSpace(providerName))
            {
                providerName = persistedConfig?.ActiveProvider;
            }
            if (string.IsNullOrWhiteSpace(providerName))
            {
                providerName = OpenRouterProvider.ProviderName;
            }

            // Canonicalize the name (env vars arrive in any casing) so it is a stable
            // config key; unknown names fail here with the list of registered providers.
            providerName = ProviderRegistry.Get(providerName.Trim()).Name;

            var manager = LlmClientManager.Instance;
            var providerSettings = ConfigurationManager.GetProviderSettings(persistedConfig, providerName);

            // Install the client without a liveness probe: a transient network blip or a
            // provider outage should degrade to per-request errors, not refuse to launch.
            // Client construction still fails hard on unusable settings (e.g. missing key).
            var swap = await manager.SwapAsync(providerName, providerSettings, requireValidation: false);
            if (!swap.Success)
            {
                throw new InvalidOperationException(swap.Error);
            }

            AgentManager.Instance.Initialize(manager);
            Saturn.Core.Agents.AgentPoolManager.Initialize(manager);

            var capabilities = manager.Current.Capabilities;

            if (!await manager.Current.ValidateConnectionAsync())
            {
                Console.WriteLine($"Warning: could not verify the connection to {capabilities.ProviderName}. " +
                    "Starting anyway; requests will fail until it is reachable. " +
                    "You can switch providers from Agent -> Provider... in the UI.");
            }

            var model = await ResolveStartupModelAsync(manager, persistedConfig, providerName);

            var temperature = 0.15;
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
                ClientSource = manager,
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

            if (persistedConfig != null)
            {
                ConfigurationManager.ApplyToAgentConfiguration(agentConfig, persistedConfig);

                // The flat persisted model may belong to a different provider; the
                // per-provider resolution above is authoritative.
                agentConfig.Model = model;
            }
            else
            {
                await ConfigurationManager.SaveConfigurationAsync(
                    ConfigurationManager.FromAgentConfiguration(agentConfig));
            }

            await ConfigurationManager.SaveProviderSelectionAsync(providerName, providerSettings, model);

            if (agentConfig.Model.Contains("gpt-5", StringComparison.OrdinalIgnoreCase))
            {
                agentConfig.Temperature = 1.0;
            }

            return (new Agent(agentConfig), manager);
        }

        /// <summary>
        /// Picks the model for the connected provider: the model remembered for it,
        /// else the provider default, else the first available model (preferring ones
        /// already loaded on local servers).
        /// </summary>
        static async Task<string> ResolveStartupModelAsync(
            ILlmClientSource manager,
            Saturn.Configuration.Objects.PersistedAgentConfiguration? persistedConfig,
            string providerName)
        {
            var capabilities = manager.Current.Capabilities;

            var model = ConfigurationManager.GetProviderModel(persistedConfig, providerName);
            if (string.IsNullOrWhiteSpace(model))
            {
                model = capabilities.DefaultModel;
            }

            if (string.IsNullOrWhiteSpace(model))
            {
                List<ModelInfo> models;
                try
                {
                    models = await manager.Current.ListModelsAsync();
                }
                catch
                {
                    // Provider unreachable at startup; leave the model unset and let the
                    // user pick one from the UI once it comes back.
                    Console.WriteLine($"Warning: could not list models from {capabilities.ProviderName}. " +
                        "Select a model via Agent -> Select Model... once the provider is reachable.");
                    return string.Empty;
                }

                model = models.FirstOrDefault(m => m.IsLoaded == true)?.Id ?? models.FirstOrDefault()?.Id;
                if (string.IsNullOrWhiteSpace(model))
                {
                    throw new InvalidOperationException(
                        $"{capabilities.ProviderName} reports no available models. Load a model and try again.");
                }
                Console.WriteLine($"No model configured for {capabilities.ProviderName}; using '{model}'.");
            }
            else if (string.IsNullOrWhiteSpace(capabilities.DefaultModel))
            {
                // Local providers: the remembered model may have been deleted since last run.
                var resolved = await ModelCatalog.ResolveModelAsync(manager, model);
                if (!string.Equals(resolved, model, StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"Model '{model}' is not available on {capabilities.ProviderName}; using '{resolved}'.");
                    model = resolved;
                }
            }

            return model;
        }
    }
}
