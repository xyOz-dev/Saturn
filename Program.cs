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

                if (TryParseWebOptions(args, out var port))
                {
                    agent.IsOrchestrator = true;
                    // Long-horizon web sessions benefit from a deeper context window.
                    agent.Configuration.MaxHistoryMessages = 50;
                    var server = new Saturn.Web.WebServer(agent, client, port);
                    Console.WriteLine($"Saturn web UI running at {server.Url}");
                    Console.WriteLine("Press Ctrl+C to stop.");
                    TryOpenBrowser(server.Url);
                    await server.RunAsync();
                    return;
                }

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
        

        static bool TryParseWebOptions(string[] args, out int port)
        {
            port = 5225;
            var webRequested = false;

            for (var i = 0; i < args.Length; i++)
            {
                if (args[i] is "--web" or "-w")
                {
                    webRequested = true;
                }
                else if (args[i] == "--port" && i + 1 < args.Length && int.TryParse(args[i + 1], out var parsed))
                {
                    port = parsed;
                    i++;
                }
            }

            return webRequested;
        }

        static void TryOpenBrowser(string url)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch
            {
                // Opening the browser is best-effort; the URL is printed either way.
            }
        }

        static async Task<(Agent, ILlmClientSource)> CreateAgent()
        {
            ProviderRegistry.Register(new OpenRouterProvider());
            ProviderRegistry.Register(new LMStudioProvider());

            var persistedConfig = await ConfigurationManager.LoadConfigurationAsync();

            var providerName = Environment.GetEnvironmentVariable("SATURN_PROVIDER");
            if (string.IsNullOrWhiteSpace(providerName))
            {
                providerName = persistedConfig?.ActiveProvider;
            }
            if (string.IsNullOrWhiteSpace(providerName))
            {
                providerName = OpenRouterProvider.ProviderName;
            }

            providerName = ProviderRegistry.Get(providerName.Trim()).Name;

            var manager = LlmClientManager.Instance;
            var providerSettings = ConfigurationManager.GetProviderSettings(persistedConfig, providerName);

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
   - Bug Fixes: **ALWAYS** verify the reported issue in the code before changing anything - users are **NOT** always correct. Reproduce or locate the fault yourself first; delegate verification to a sub-agent only when it is large enough to be worth running in parallel.
   - Unit Tests: **ALWAYS** review the code provided and evaluate the issues. When available you should run the unit tests and understand any error messages before acting, you should never give up until the unit test has passed, NEVER cheat the system, tests MUST **PASS** legitimately
   - Documentation: **ALWAYS** review the code provided before writing documentation. The documentation written should always be validated in code.
2) Output Rules:
   - **NEVER** Use emojis.

Task System (web mode)
- The user maintains durable todo lists via the task tools: list_tasks, create_task, update_task, complete_task, wait_for_task, claim_task, dispatch_task, list_due_tasks.
- Scopes: 'global' (machine-wide), 'project' (this repository, named boards), 'agent' (per-agent lists).
- [Saturn Scheduler] messages are automated wake-ups: a recurring task fired, a task unblocked, a dispatched task completed, or an agent-available task is ready. Act on them using the task tools, then report concisely.
- Tasks flagged requires-approval: call claim_task and WAIT for the user's decision (you will be woken). Never work on user-handoff-only tasks.
- For long-running dependencies prefer wait_for_task over polling: register, end your turn, and you will be re-prompted with the result when it completes.
- Keep the task list accurate: complete_task when work finishes, create_task for follow-ups you discover.

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
   
4) CRITICAL: Integrating sub-agent work:
   - DO NOT recreate or redo work completed by sub-agents
   - Files written by sub-agents are already saved - do not rewrite them from scratch
   - Review each sub-agent's report and verify it satisfies the task requirements
   - If the work needs large changes, hand it back to the same agent with specific feedback and the reason; make small corrections yourself
   - Integrate results from parallel agents into a coherent whole - integrate, never duplicate

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
   - Track multi-step work with update_todos: write the step list up front, keep exactly one item in_progress, and mark steps completed immediately.
   - The update_todos list is your private, session-scoped scratchpad; use the task tools for the user's durable todo lists.
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
                    "get_command_output", "kill_command", "web_fetch", "web_search",
                    "list_tasks", "create_task", "update_task", "complete_task",
                    "wait_for_task", "claim_task", "dispatch_task", "list_due_tasks",
                    "update_todos"
                },
            };

            // Persisted configs predate newer tools; backfill only those.
            // Re-adding the full default set would silently restore tools
            // the user deliberately removed (e.g. execute_command).
            var backfillTools = new[]
            {
                "list_tasks", "create_task", "update_task", "complete_task",
                "wait_for_task", "claim_task", "dispatch_task", "list_due_tasks",
                "update_todos", "web_search"
            };

            if (persistedConfig != null)
            {
                ConfigurationManager.ApplyToAgentConfiguration(agentConfig, persistedConfig);

                agentConfig.Model = model;

                agentConfig.ToolNames = agentConfig.ToolNames
                    .Union(backfillTools, StringComparer.OrdinalIgnoreCase)
                    .ToList();
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
