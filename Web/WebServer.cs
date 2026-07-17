using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Saturn.Agents;
using Saturn.Agents.MultiAgent;
using Saturn.Core.Approvals;
using Saturn.Core.Tasks;
using Saturn.Data;
using Saturn.Data.Tasks;
using Saturn.Providers;
using Saturn.Tools.Core;
using Saturn.Tools.Search;

namespace Saturn.Web
{
    public class WebServer
    {
        private readonly Agent _rootAgent;
        private readonly ILlmClientSource _clientSource;
        private readonly int _port;
        private readonly DateTime _startedAt = DateTime.UtcNow;

        // Per-process API token: injected into index.html and required on every
        // /api request, so pages on other origins (and other local users) cannot
        // drive the agent API even if they can reach the port.
        private readonly string _apiToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

        private readonly EventHub _hub = new();
        private readonly TaskStore _tasks = new();
        private readonly OrchestratorService _orchestrator;
        private readonly WebCommandApprovalService _approvals;
        private readonly ChatHistoryRepository _history = new();
        private readonly Saturn.Config.TaskSystemSettings _taskSettings = Saturn.Config.TaskSystemSettings.Load();
        private readonly TaskCoordinator _coordinator;
        private readonly TaskSchedulerService _scheduler;
        private readonly CommandJudge _judge;
        private readonly ApprovalCoordinator _approvalCoordinator;

        public WebServer(Agent rootAgent, ILlmClientSource clientSource, int port)
        {
            _rootAgent = rootAgent;
            _clientSource = clientSource;
            _port = port;
            _orchestrator = new OrchestratorService(rootAgent, _hub);
            _approvals = new WebCommandApprovalService(_hub, _taskSettings);
            _coordinator = new TaskCoordinator(_tasks, _orchestrator, _hub, _taskSettings);
            _scheduler = new TaskSchedulerService(_coordinator, _taskSettings);
            _judge = new CommandJudge(clientSource, () => _rootAgent.Configuration.Model);
            _approvalCoordinator = new ApprovalCoordinator(_approvals, _judge, _taskSettings, _hub, _tasks);
            _coordinator.OnClaimApprovalNeeded = task =>
            {
                _approvalCoordinator.RequestTaskClaimApproval(task,
                    approved => _ = _coordinator.ResolveClaimAsync(task.Id, approved));
                return Task.CompletedTask;
            };
        }

        public string Url => $"http://localhost:{_port}";

        public async Task RunAsync(CancellationToken cancellationToken = default, Action? onReady = null)
        {
            CommandApprovalService.GlobalOverride = _approvalCoordinator;
            TaskSystem.Store = _tasks;
            TaskSystem.Coordinator = _coordinator;
            _tasks.OnTaskChanged += (change, task) =>
            {
                _hub.Publish("tasks.changed", new { taskId = task.Id, scope = task.Scope, board = task.Board, change });
                // Recurring tasks reset to pending on completion (non-terminal),
                // but their dependents still need the unblock sweep.
                if (change == "completed")
                {
                    _ = _coordinator.HandleSaturnTaskCompletedAsync(task);
                }
            };
            var imported = await _tasks.ImportLegacyTodosAsync();
            if (imported > 0)
            {
                Console.WriteLine($"Imported {imported} legacy todos into the task system.");
            }
            WireAgentManagerEvents();
            await _orchestrator.RestoreTranscriptAsync(_history);
            await _coordinator.StartAsync();
            _scheduler.Start();

            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls(Url);
            builder.Services.ConfigureHttpJsonOptions(options =>
            {
                options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            });

            var app = builder.Build();

            app.Use(async (context, next) =>
            {
                // A non-loopback Host header means the browser resolved some public
                // DNS name to 127.0.0.1 (DNS rebinding); such requests bypass CORS,
                // so reject them outright.
                if (!IsLoopbackHost(context.Request.Host.Host))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await context.Response.WriteAsJsonAsync(new { error = "Forbidden host" });
                    return;
                }

                context.Response.Headers["X-Content-Type-Options"] = "nosniff";
                context.Response.Headers["Referrer-Policy"] = "no-referrer";

                if (context.Request.Path.StartsWithSegments("/api"))
                {
                    // EventSource cannot set headers, so the SSE stream passes the
                    // token as a query parameter instead.
                    var token = context.Request.Headers["X-Saturn-Token"].ToString();
                    if (string.IsNullOrEmpty(token))
                    {
                        token = context.Request.Query["token"].ToString();
                    }
                    if (!TokenMatches(token))
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        await context.Response.WriteAsJsonAsync(new { error = "Missing or invalid API token" });
                        return;
                    }
                }

                await next();
            });

            MapStaticAssets(app);
            MapApi(app);

            if (onReady != null)
            {
                app.Lifetime.ApplicationStarted.Register(onReady);
            }

            await app.RunAsync(cancellationToken);
        }

        private static bool IsLoopbackHost(string host)
        {
            return host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                || host == "127.0.0.1"
                || host == "::1"
                || host == "[::1]";
        }

        private bool TokenMatches(string provided)
        {
            if (string.IsNullOrEmpty(provided))
            {
                return false;
            }
            var a = Encoding.UTF8.GetBytes(provided);
            var b = Encoding.UTF8.GetBytes(_apiToken);
            return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
        }

        private void WireAgentManagerEvents()
        {
            var manager = AgentManager.Instance;
            manager.OnAgentCreated += (agentId, name) =>
                _hub.Publish("agent.created", new { agentId, name });
            manager.OnAgentStatusChanged += (agentId, name, status) =>
                _hub.Publish("agent.status", new { agentId, name, status });
            manager.OnTaskCompleted += (taskId, result) =>
            {
                _hub.Publish("task.completed", new
                {
                    taskId,
                    result.AgentId,
                    result.AgentName,
                    result.Description,
                    result.Success,
                    result.CompletedAt,
                    durationSeconds = result.Duration.TotalSeconds
                });
                // Attach the full result to the orchestrator transcript so the
                // user can see what actually happened without leaving the chat.
                _orchestrator.AddTaskResultEntry(taskId, result.AgentName, result.Success, result.Result);
            };
        }

        private void MapApi(WebApplication app)
        {
            var api = app.MapGroup("/api");
            var manager = AgentManager.Instance;

            api.MapGet("/events", async (HttpContext context) =>
                await _hub.StreamAsync(context.Response, context.RequestAborted));

            api.MapGet("/overview", async () =>
            {
                var contexts = manager.GetAgentContexts();
                var completed = manager.GetCompletedTasks();
                var todos = (await _tasks.ListAsync()).Select(v => v.Task).ToList();
                return Results.Ok(new
                {
                    provider = _clientSource.ActiveProviderName,
                    connected = _clientSource.IsConnected,
                    model = _rootAgent.Configuration.Model,
                    uptimeSeconds = (DateTime.UtcNow - _startedAt).TotalSeconds,
                    agents = new
                    {
                        total = contexts.Count,
                        working = contexts.Count(c => c.CurrentTask != null),
                        idle = contexts.Count(c => c.CurrentTask == null),
                        max = manager.GetMaxConcurrentAgents()
                    },
                    tasks = new
                    {
                        running = contexts.Count(c => c.CurrentTask != null),
                        completed = completed.Count(t => t.Success),
                        failed = completed.Count(t => !t.Success)
                    },
                    todos = new
                    {
                        open = todos.Count(t => !TaskStatuses.IsTerminal(t.Status)),
                        done = todos.Count(t => TaskStatuses.IsTerminal(t.Status))
                    },
                    orchestratorBusy = _orchestrator.IsBusy,
                    pendingApprovals = _approvals.GetPending().Count
                });
            });

            api.MapGet("/agents", () => Results.Ok(ProjectAgents()));

            api.MapPost("/agents", async (CreateAgentRequest request) =>
            {
                if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Purpose))
                {
                    return Results.BadRequest(new { error = "name and purpose are required" });
                }

                var model = string.IsNullOrWhiteSpace(request.Model)
                    ? _rootAgent.Configuration.Model
                    : request.Model;

                var (success, result, _) = await manager.TryCreateSubAgent(
                    request.Name.Trim(),
                    request.Purpose.Trim(),
                    model,
                    enableTools: true,
                    temperature: request.Temperature,
                    maxTokens: request.MaxTokens,
                    systemPromptOverride: string.IsNullOrWhiteSpace(request.SystemPrompt) ? null : request.SystemPrompt);

                return success
                    ? Results.Ok(new { agentId = result })
                    : Results.Conflict(new { error = result });
            });

            api.MapDelete("/agents/{agentId}", (string agentId) =>
            {
                var exists = manager.GetAgentStatus(agentId).Exists;
                if (!exists)
                {
                    return Results.NotFound(new { error = $"Agent {agentId} not found" });
                }
                manager.TerminateAgent(agentId);
                return Results.Ok(new { terminated = agentId });
            });

            api.MapPost("/agents/terminate-all", () =>
            {
                var count = manager.GetCurrentAgentCount();
                manager.TerminateAllAgents();
                _hub.Publish("agents.cleared", new { terminated = count });
                return Results.Ok(new { terminated = count });
            });

            api.MapPost("/agents/{agentId}/handoff", async (string agentId, HandOffRequest request) =>
            {
                if (string.IsNullOrWhiteSpace(request.Task))
                {
                    return Results.BadRequest(new { error = "task is required" });
                }

                try
                {
                    var taskId = await manager.HandOffTask(agentId, request.Task.Trim(), request.Context);
                    return Results.Ok(new { taskId });
                }
                catch (InvalidOperationException ex)
                {
                    return Results.NotFound(new { error = ex.Message });
                }
            });

            api.MapGet("/tasks", () =>
            {
                var running = manager.GetAgentContexts()
                    .Where(c => c.CurrentTask != null)
                    .Select(c => new
                    {
                        taskId = c.CurrentTask!.Id,
                        agentId = c.Id,
                        agentName = c.Name,
                        description = c.CurrentTask.Description,
                        startedAt = c.CurrentTask.StartedAt,
                        status = c.CurrentTask.Status.ToString()
                    })
                    .OrderBy(t => t.startedAt)
                    .ToList();

                var completed = manager.GetCompletedTasks()
                    .OrderByDescending(t => t.CompletedAt)
                    .Select(t => new
                    {
                        taskId = t.TaskId,
                        agentId = t.AgentId,
                        agentName = t.AgentName,
                        description = t.Description,
                        success = t.Success,
                        result = t.Result,
                        completedAt = t.CompletedAt,
                        durationSeconds = t.Duration.TotalSeconds
                    })
                    .ToList();

                return Results.Ok(new { running, completed });
            });

            api.MapGet("/tasks/{taskId}", (string taskId) =>
            {
                var result = manager.GetTaskResult(taskId);
                return result == null
                    ? Results.NotFound(new { error = $"Task {taskId} not found" })
                    : Results.Ok(result);
            });

            api.MapPost("/tasks/clear-completed", () =>
            {
                manager.ClearCompletedTasks();
                _hub.Publish("tasks.cleared");
                return Results.Ok();
            });

            api.MapGet("/providers", async () =>
            {
                var persisted = await Saturn.Configuration.ConfigurationManager.LoadConfigurationAsync();
                return Results.Ok(ProviderRegistry.All.Select(p =>
                {
                    var saved = Saturn.Configuration.ConfigurationManager.GetProviderSettings(persisted, p.Name);
                    return new
                    {
                        name = p.Name,
                        displayName = p.DisplayName,
                        active = string.Equals(_clientSource.ActiveProviderName, p.Name, StringComparison.OrdinalIgnoreCase),
                        model = Saturn.Configuration.ConfigurationManager.GetProviderModel(persisted, p.Name),
                        settings = p.SettingDescriptors.Select(d => new
                        {
                            key = d.Key,
                            label = d.Label,
                            kind = d.Kind.ToString().ToLowerInvariant(),
                            required = d.Required,
                            defaultValue = d.DefaultValue,
                            environmentVariable = d.EnvironmentVariable,
                            configured = !string.IsNullOrWhiteSpace(d.Resolve(saved)),
                            // Secrets are never echoed back to the browser.
                            value = d.Kind == ProviderSettingKind.Secret ? null : saved.Get(d.Key)
                        })
                    };
                }));
            });

            api.MapPost("/providers/switch", async (ProviderSwitchRequest request) =>
            {
                if (!ProviderRegistry.TryGet(request.Provider ?? "", out var provider))
                {
                    return Results.NotFound(new { error = $"Unknown provider '{request.Provider}'" });
                }

                var persisted = await Saturn.Configuration.ConfigurationManager.LoadConfigurationAsync();
                var settings = Saturn.Configuration.ConfigurationManager.GetProviderSettings(persisted, provider.Name);
                if (request.Settings != null)
                {
                    foreach (var kvp in request.Settings)
                    {
                        settings.Set(kvp.Key, kvp.Value);
                    }
                }

                var swap = await LlmClientManager.Instance.SwapAsync(provider.Name, settings, requireValidation: true);
                if (!swap.Success)
                {
                    return Results.BadRequest(new { error = swap.Error ?? "Provider switch failed" });
                }

                var model = request.Model;
                if (string.IsNullOrWhiteSpace(model))
                {
                    model = Saturn.Configuration.ConfigurationManager.GetProviderModel(persisted, provider.Name)
                        ?? _clientSource.Current.Capabilities.DefaultModel;
                }
                if (string.IsNullOrWhiteSpace(model))
                {
                    try
                    {
                        var available = await _clientSource.Current.ListModelsAsync();
                        model = available.FirstOrDefault(m => m.IsLoaded == true)?.Id ?? available.FirstOrDefault()?.Id;
                    }
                    catch
                    {
                        model = null;
                    }
                }
                if (!string.IsNullOrWhiteSpace(model))
                {
                    model = await ModelCatalog.ResolveModelAsync(_clientSource, model);
                }

                _rootAgent.Configuration.Model = model ?? "";
                manager.SetParentModel(model);
                await Saturn.Configuration.ConfigurationManager.SaveProviderSelectionAsync(provider.Name, settings, model);
                _hub.Publish("provider.changed", new { provider = _clientSource.ActiveProviderName, model });
                return Results.Ok(new { provider = _clientSource.ActiveProviderName, model, connected = _clientSource.IsConnected });
            });

            api.MapGet("/search-providers", async () =>
            {
                var persisted = await Saturn.Configuration.ConfigurationManager.LoadConfigurationAsync();
                return Results.Ok(SearchProviderRegistry.All.Select(p =>
                {
                    var saved = Saturn.Configuration.ConfigurationManager.GetSearchProviderSettings(persisted, p.Name);
                    return new
                    {
                        name = p.Name,
                        displayName = p.DisplayName,
                        active = string.Equals(persisted?.SearchProvider, p.Name, StringComparison.OrdinalIgnoreCase),
                        settings = p.SettingDescriptors.Select(d => new
                        {
                            key = d.Key,
                            label = d.Label,
                            kind = d.Kind.ToString().ToLowerInvariant(),
                            required = d.Required,
                            defaultValue = d.DefaultValue,
                            environmentVariable = d.EnvironmentVariable,
                            configured = !string.IsNullOrWhiteSpace(d.Resolve(saved)),
                            // Secrets are never echoed back to the browser.
                            value = d.Kind == ProviderSettingKind.Secret ? null : saved.Get(d.Key)
                        })
                    };
                }));
            });

            api.MapPost("/search-providers/switch", async (SearchProviderSwitchRequest request) =>
            {
                if (!SearchProviderRegistry.TryGet(request.Provider ?? "", out var provider))
                {
                    return Results.NotFound(new { error = $"Unknown search provider '{request.Provider}'" });
                }

                var persisted = await Saturn.Configuration.ConfigurationManager.LoadConfigurationAsync();
                var settings = Saturn.Configuration.ConfigurationManager.GetSearchProviderSettings(persisted, provider.Name);
                if (request.Settings != null)
                {
                    foreach (var kvp in request.Settings)
                    {
                        // Only overwrite with non-empty values so a blank secret field keeps the stored key.
                        if (!string.IsNullOrWhiteSpace(kvp.Value))
                        {
                            settings.Set(kvp.Key, kvp.Value);
                        }
                    }
                }

                var missing = provider.SettingDescriptors
                    .FirstOrDefault(d => d.Required && string.IsNullOrWhiteSpace(d.Resolve(settings)));
                if (missing != null)
                {
                    return Results.BadRequest(new { error = $"{missing.Label} is required for {provider.DisplayName}." });
                }

                await Saturn.Configuration.ConfigurationManager.SaveSearchProviderSelectionAsync(provider.Name, settings);
                _hub.Publish("search.provider.changed", new { provider = provider.Name });
                return Results.Ok(new { provider = provider.Name, configured = true });
            });

            api.MapPost("/model", async (ModelSwitchRequest request) =>
            {
                if (string.IsNullOrWhiteSpace(request.Model))
                {
                    return Results.BadRequest(new { error = "model is required" });
                }
                var model = await ModelCatalog.ResolveModelAsync(_clientSource, request.Model.Trim());
                _rootAgent.Configuration.Model = model;
                manager.SetParentModel(model);
                var persisted = await Saturn.Configuration.ConfigurationManager.LoadConfigurationAsync();
                var settings = Saturn.Configuration.ConfigurationManager.GetProviderSettings(persisted, _clientSource.ActiveProviderName);
                await Saturn.Configuration.ConfigurationManager.SaveProviderSelectionAsync(_clientSource.ActiveProviderName, settings, model);
                _hub.Publish("provider.changed", new { provider = _clientSource.ActiveProviderName, model });
                return Results.Ok(new { model });
            });

            api.MapGet("/models", async () =>
            {
                try
                {
                    var models = await _clientSource.Current.ListModelsAsync();
                    return Results.Ok(models.Select(m => new
                    {
                        id = m.Id,
                        displayName = m.DisplayName ?? m.Id,
                        contextLength = m.ContextLength,
                        isLoaded = m.IsLoaded
                    }));
                }
                catch (Exception ex)
                {
                    return Results.Problem($"Could not list models: {ex.Message}");
                }
            });

            api.MapGet("/todos", async (string? scope, string? board, string? status, bool? includeDone) =>
            {
                var views = await _tasks.ListAsync(scope, board, status, includeDone ?? true);
                return Results.Ok(views.Select(ProjectTaskView));
            });

            api.MapGet("/todos/boards", async () =>
            {
                var projectBoards = await _tasks.GetBoardsAsync(TaskScopes.Project);
                var agentBoards = await _tasks.GetBoardsAsync(TaskScopes.Agent);
                return Results.Ok(new { project = projectBoards, agent = agentBoards });
            });

            api.MapGet("/todos/validate-cron", (string expr) =>
            {
                if (!RecurrenceCalculator.TryParseCron(expr, out _))
                {
                    return Results.Ok(new { valid = false, error = $"Invalid cron expression: '{expr}'" });
                }
                var nextRuns = new List<DateTime>();
                var after = DateTime.UtcNow;
                for (var i = 0; i < 3; i++)
                {
                    var next = RecurrenceCalculator.GetNextOccurrenceUtc(RecurrenceKinds.Cron, null, expr, after);
                    if (next == null) break;
                    nextRuns.Add(next.Value);
                    after = next.Value;
                }
                return Results.Ok(new { valid = true, nextRuns });
            });

            api.MapGet("/todos/{id}", async (string id) =>
            {
                var task = await _tasks.FindAsync(id);
                if (task == null)
                {
                    return Results.NotFound(new { error = $"Task {id} not found" });
                }
                var view = await _tasks.BuildViewAsync(task);
                var dependents = (await _tasks.Project.GetDependentsAsync(id))
                    .Concat(await _tasks.Global.GetDependentsAsync(id)).Distinct().ToList();
                var runs = await _tasks.RepoOf(task).GetRunsAsync(id);
                var dispatches = await _tasks.Project.GetDispatchesForTaskAsync(id);
                var waiters = await _tasks.Project.GetPendingWaitersAsync(id);
                return Results.Ok(new
                {
                    task = ProjectTaskView(view),
                    dependents,
                    runs,
                    dispatches,
                    waiters
                });
            });

            api.MapPost("/todos", async (TaskCreateRequest request) =>
            {
                try
                {
                    var task = await _tasks.CreateAsync(new TaskCreateSpec
                    {
                        Title = request.Title ?? "",
                        Notes = request.Notes,
                        Scope = request.Scope ?? TaskScopes.Project,
                        Board = request.Board ?? "default",
                        Priority = request.Priority ?? "normal",
                        CreatedBy = "user",
                        BlockedBy = request.BlockedBy ?? new List<string>(),
                        RecurrenceKind = request.RecurrenceKind ?? RecurrenceKinds.None,
                        RecurrenceIntervalSeconds = request.RecurrenceIntervalSeconds,
                        RecurrenceCron = request.RecurrenceCron,
                        CatchUpPolicy = request.CatchUpPolicy ?? CatchUpPolicies.RunOnce,
                        AgentAvailable = request.AgentAvailable ?? false,
                        RequiresApproval = request.RequiresApproval ?? false,
                        UserHandoffOnly = request.UserHandoffOnly ?? false
                    });
                    return Results.Ok(ProjectTaskView(await _tasks.BuildViewAsync(task)));
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }
            });

            api.MapPatch("/todos/{id}", async (string id, TaskUpdateRequest request) =>
            {
                try
                {
                    var task = await _tasks.UpdateAsync(id, new TaskUpdateSpec
                    {
                        Title = request.Title,
                        Notes = request.Notes,
                        Status = request.Status,
                        Priority = request.Priority,
                        Scope = request.Scope,
                        Board = request.Board,
                        SortOrder = request.SortOrder,
                        BlockedBy = request.BlockedBy,
                        RecurrenceKind = request.RecurrenceKind,
                        RecurrenceIntervalSeconds = request.RecurrenceIntervalSeconds,
                        RecurrenceCron = request.RecurrenceCron,
                        CatchUpPolicy = request.CatchUpPolicy,
                        AgentAvailable = request.AgentAvailable,
                        RequiresApproval = request.RequiresApproval,
                        UserHandoffOnly = request.UserHandoffOnly
                    });
                    return task == null
                        ? Results.NotFound(new { error = $"Task {id} not found" })
                        : Results.Ok(ProjectTaskView(await _tasks.BuildViewAsync(task)));
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }
                catch (InvalidOperationException ex)
                {
                    return Results.Conflict(new { error = ex.Message });
                }
            });

            api.MapPost("/todos/{id}/complete", async (string id, TaskCompleteRequest request) =>
            {
                try
                {
                    var task = await _tasks.CompleteAsync(id, request.Success ?? true, request.Note);
                    return task == null
                        ? Results.NotFound(new { error = $"Task {id} not found" })
                        : Results.Ok(ProjectTaskView(await _tasks.BuildViewAsync(task)));
                }
                catch (InvalidOperationException ex)
                {
                    return Results.Conflict(new { error = ex.Message });
                }
            });

            api.MapPost("/todos/{id}/dispatch", async (string id, TaskDispatchRequest request) =>
            {
                if (string.IsNullOrWhiteSpace(request.AgentId))
                {
                    return Results.BadRequest(new { error = "agentId is required" });
                }
                var status = manager.GetAgentStatus(request.AgentId);
                if (!status.Exists)
                {
                    return Results.NotFound(new { error = $"Agent {request.AgentId} not found" });
                }
                if (!status.IsIdle)
                {
                    return Results.Conflict(new { error = $"Agent {status.Name} is busy" });
                }
                // Manual user handoff bypasses the userHandoffOnly restriction by design.
                var (ok, message, _) = await _coordinator.DispatchTaskAsync(id, request.AgentId, status.Name, userInitiated: true);
                return ok ? Results.Ok(new { message }) : Results.BadRequest(new { error = message });
            });

            api.MapDelete("/todos/{id}", async (string id) =>
            {
                return await _tasks.DeleteAsync(id)
                    ? Results.Ok()
                    : Results.NotFound(new { error = $"Task {id} not found" });
            });

            api.MapPost("/todos/clear-completed", async (string? scope, string? board) =>
            {
                var views = await _tasks.ListAsync(scope, board);
                var removed = 0;
                foreach (var view in views.Where(v => TaskStatuses.IsTerminal(v.Task.Status)))
                {
                    if (await _tasks.DeleteAsync(view.Task.Id))
                    {
                        removed++;
                    }
                }
                return Results.Ok(new { removed });
            });

            api.MapPost("/orchestrator/message", (OrchestratorMessageRequest request) =>
            {
                if (string.IsNullOrWhiteSpace(request.Message))
                {
                    return Results.BadRequest(new { error = "message is required" });
                }
                return _orchestrator.TrySend(request.Message.Trim())
                    ? Results.Accepted()
                    : Results.Conflict(new { error = "Orchestrator is busy; cancel the current run first" });
            });

            api.MapPost("/orchestrator/cancel", () =>
            {
                _orchestrator.Cancel();
                return Results.Ok();
            });

            api.MapGet("/orchestrator/transcript", () => Results.Ok(new
            {
                busy = _orchestrator.IsBusy,
                model = _orchestrator.Model,
                sessionId = _orchestrator.CurrentSessionId,
                entries = _orchestrator.GetTranscript()
            }));

            api.MapGet("/wake", async () => Results.Ok(await _tasks.Project.GetPendingWakesAsync()));

            api.MapDelete("/wake/{id}", async (string id) =>
            {
                return await _tasks.Project.DeleteWakeAsync(id)
                    ? Results.Ok()
                    : Results.NotFound(new { error = $"Wake {id} not found" });
            });

            api.MapGet("/approvals", () => Results.Ok(_approvals.GetPending()));

            api.MapPost("/approvals/{id}", (string id, ApprovalDecisionRequest request) =>
            {
                return _approvals.Resolve(id, request.Approved)
                    ? Results.Ok()
                    : Results.NotFound(new { error = $"Approval {id} not found or already resolved" });
            });

            api.MapGet("/sessions", async (int? limit) =>
            {
                var sessions = await _history.GetSessionsAsync(limit: limit ?? 100);
                return Results.Ok(sessions.Select(s => new
                {
                    s.Id,
                    s.Title,
                    s.ChatType,
                    s.ParentSessionId,
                    s.AgentName,
                    s.Model,
                    s.CreatedAt,
                    s.UpdatedAt,
                    s.IsActive
                }));
            });

            api.MapGet("/sessions/{id}/messages", async (string id) =>
            {
                var session = await _history.GetSessionAsync(id);
                if (session == null)
                {
                    return Results.NotFound(new { error = $"Session {id} not found" });
                }
                var messages = await _history.GetMessagesAsync(id);
                return Results.Ok(messages.Select(m => new
                {
                    m.Id,
                    m.Role,
                    m.Content,
                    m.AgentName,
                    m.Timestamp,
                    m.SequenceNumber,
                    ToolCalls = ExtractToolCallNames(m.ToolCallsJson),
                    ToolName = m.Role == "tool" ? m.Name : null
                }));
            });

            object CurrentSettings() => new
            {
                maxConcurrentAgents = manager.GetMaxConcurrentAgents(),
                requireCommandApproval = _rootAgent.Configuration.RequireCommandApproval,
                trustMode = _taskSettings.TrustMode,
                judgeEnabled = _taskSettings.JudgeEnabled,
                approvalTimeoutMinutes = _taskSettings.ApprovalTimeoutMinutes,
                schedulerIntervalSeconds = _taskSettings.SchedulerIntervalSeconds,
                maxWakesPerHour = _taskSettings.MaxWakesPerHour,
                provider = _clientSource.ActiveProviderName,
                model = _rootAgent.Configuration.Model
            };

            api.MapGet("/settings", () => Results.Ok(CurrentSettings()));

            api.MapPut("/settings", (SettingsUpdateRequest request) =>
            {
                if (request.MaxConcurrentAgents.HasValue)
                {
                    manager.SetMaxConcurrentAgents(request.MaxConcurrentAgents.Value);
                }
                if (request.RequireCommandApproval.HasValue)
                {
                    _rootAgent.Configuration.RequireCommandApproval = request.RequireCommandApproval.Value;
                }

                var taskSettingsChanged = false;
                if (request.TrustMode.HasValue) { _taskSettings.TrustMode = request.TrustMode.Value; taskSettingsChanged = true; }
                if (request.JudgeEnabled.HasValue) { _taskSettings.JudgeEnabled = request.JudgeEnabled.Value; taskSettingsChanged = true; }
                if (request.ApprovalTimeoutMinutes.HasValue) { _taskSettings.ApprovalTimeoutMinutes = Math.Max(0, request.ApprovalTimeoutMinutes.Value); taskSettingsChanged = true; }
                if (request.SchedulerIntervalSeconds.HasValue) { _taskSettings.SchedulerIntervalSeconds = Math.Max(5, request.SchedulerIntervalSeconds.Value); taskSettingsChanged = true; }
                if (request.MaxWakesPerHour.HasValue) { _taskSettings.MaxWakesPerHour = Math.Max(1, request.MaxWakesPerHour.Value); taskSettingsChanged = true; }
                if (taskSettingsChanged)
                {
                    _taskSettings.Save();
                }

                _hub.Publish("settings.changed", CurrentSettings());
                return Results.Ok(CurrentSettings());
            });

            // ---------- agent generation config ----------

            object CurrentAgentConfig() => new
            {
                model = _rootAgent.Configuration.Model,
                temperature = _rootAgent.Configuration.Temperature,
                maxTokens = _rootAgent.Configuration.MaxTokens,
                topP = _rootAgent.Configuration.TopP,
                enableStreaming = _rootAgent.Configuration.EnableStreaming,
                maintainHistory = _rootAgent.Configuration.MaintainHistory,
                maxHistoryMessages = _rootAgent.Configuration.MaxHistoryMessages,
                enableUserRules = _rootAgent.Configuration.EnableUserRules,
                toolNames = _rootAgent.Configuration.ToolNames
            };

            async Task PersistAgentConfigAsync()
            {
                await Saturn.Configuration.ConfigurationManager.SaveConfigurationAsync(
                    Saturn.Configuration.ConfigurationManager.FromAgentConfiguration(_rootAgent.Configuration));
            }

            api.MapGet("/agent-config", () => Results.Ok(CurrentAgentConfig()));

            api.MapPut("/agent-config", async (AgentConfigUpdateRequest request) =>
            {
                var cfg = _rootAgent.Configuration;
                if (request.Temperature.HasValue) cfg.Temperature = Math.Clamp(request.Temperature.Value, 0, 2);
                if (request.MaxTokens.HasValue) cfg.MaxTokens = Math.Max(1, request.MaxTokens.Value);
                if (request.TopP.HasValue) cfg.TopP = Math.Clamp(request.TopP.Value, 0, 1);
                if (request.EnableStreaming.HasValue) cfg.EnableStreaming = request.EnableStreaming.Value;
                if (request.MaintainHistory.HasValue) cfg.MaintainHistory = request.MaintainHistory.Value;
                if (request.MaxHistoryMessages.HasValue) cfg.MaxHistoryMessages = Math.Max(2, request.MaxHistoryMessages.Value);
                if (request.EnableUserRules.HasValue)
                {
                    cfg.EnableUserRules = request.EnableUserRules.Value;
                    manager.SetParentEnableUserRules(cfg.EnableUserRules);
                }
                if (request.ToolNames != null && request.ToolNames.Count > 0)
                {
                    var known = ToolRegistry.Instance.GetAllNames().ToHashSet(StringComparer.OrdinalIgnoreCase);
                    cfg.ToolNames = request.ToolNames.Where(t => known.Contains(t)).ToList();
                }
                await PersistAgentConfigAsync();
                _hub.Publish("settings.changed", CurrentAgentConfig());
                return Results.Ok(CurrentAgentConfig());
            });

            api.MapGet("/tools", () =>
            {
                var enabled = _rootAgent.Configuration.ToolNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
                return Results.Ok(ToolRegistry.Instance.GetAll()
                    .OrderBy(t => t.Name)
                    .Select(t => new
                    {
                        name = t.Name,
                        enabled = enabled.Count == 0 || enabled.Contains(t.Name),
                        description = t.Description.Split('\n')[0]
                    }));
            });

            // ---------- sub-agent defaults ----------

            object CurrentSubAgentDefaults()
            {
                var prefs = Saturn.Config.SubAgentPreferences.Instance;
                return new
                {
                    defaultModel = prefs.DefaultModel,
                    defaultTemperature = prefs.DefaultTemperature,
                    defaultMaxTokens = prefs.DefaultMaxTokens,
                    defaultTopP = prefs.DefaultTopP,
                    defaultEnableTools = prefs.DefaultEnableTools,
                    enableReviewStage = prefs.EnableReviewStage,
                    reviewerModel = prefs.ReviewerModel,
                    reviewTimeoutSeconds = prefs.ReviewTimeoutSeconds,
                    maxRevisionCycles = prefs.MaxRevisionCycles
                };
            }

            api.MapGet("/subagent-defaults", () => Results.Ok(CurrentSubAgentDefaults()));

            api.MapPut("/subagent-defaults", (SubAgentDefaultsRequest request) =>
            {
                var prefs = Saturn.Config.SubAgentPreferences.Instance;
                if (!string.IsNullOrWhiteSpace(request.DefaultModel)) prefs.DefaultModel = request.DefaultModel.Trim();
                if (request.DefaultTemperature.HasValue) prefs.DefaultTemperature = Math.Clamp(request.DefaultTemperature.Value, 0, 2);
                if (request.DefaultMaxTokens.HasValue) prefs.DefaultMaxTokens = Math.Max(1, request.DefaultMaxTokens.Value);
                if (request.DefaultTopP.HasValue) prefs.DefaultTopP = Math.Clamp(request.DefaultTopP.Value, 0, 1);
                if (request.DefaultEnableTools.HasValue) prefs.DefaultEnableTools = request.DefaultEnableTools.Value;
                if (request.EnableReviewStage.HasValue) prefs.EnableReviewStage = request.EnableReviewStage.Value;
                if (request.ReviewerModel != null) prefs.ReviewerModel = string.IsNullOrWhiteSpace(request.ReviewerModel) ? null : request.ReviewerModel.Trim();
                if (request.ReviewTimeoutSeconds.HasValue) prefs.ReviewTimeoutSeconds = Math.Max(30, request.ReviewTimeoutSeconds.Value);
                if (request.MaxRevisionCycles.HasValue) prefs.MaxRevisionCycles = Math.Clamp(request.MaxRevisionCycles.Value, 0, 10);
                prefs.Save();
                _hub.Publish("settings.changed", CurrentSubAgentDefaults());
                return Results.Ok(CurrentSubAgentDefaults());
            });

            // ---------- user rules ----------

            api.MapGet("/user-rules", async () =>
            {
                var (content, wasTruncated, error) = await Saturn.Core.UserRulesManager.LoadUserRules();
                return Results.Ok(new
                {
                    content,
                    wasTruncated,
                    error,
                    path = Saturn.Core.UserRulesManager.GetRulesFilePath(),
                    enabled = _rootAgent.Configuration.EnableUserRules
                });
            });

            api.MapPut("/user-rules", async (UserRulesUpdateRequest request) =>
            {
                try
                {
                    await Saturn.Core.UserRulesManager.SaveRulesAsync(request.Content ?? "");
                    return Results.Ok(new { saved = true });
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }
            });

            // ---------- modes ----------

            api.MapGet("/modes", () => Results.Ok(Saturn.Agents.Core.ModeManager.Instance.GetAllModes()
                .Select(m => new
                {
                    id = m.Id,
                    name = m.Name,
                    description = m.Description,
                    model = m.Model,
                    temperature = m.Temperature,
                    requireCommandApproval = m.RequireCommandApproval,
                    toolCount = m.ToolNames?.Count
                })));

            api.MapPost("/modes/{id:guid}/apply", async (Guid id) =>
            {
                var mode = Saturn.Agents.Core.ModeManager.Instance.GetMode(id);
                if (mode == null)
                {
                    return Results.NotFound(new { error = $"Mode {id} not found" });
                }
                Saturn.Agents.Core.ModeManager.Instance.ApplyModeToConfiguration(id, _rootAgent.Configuration);
                var model = await ModelCatalog.ResolveModelAsync(_clientSource, _rootAgent.Configuration.Model);
                _rootAgent.Configuration.Model = model;
                manager.SetParentModel(model);
                manager.SetParentEnableUserRules(_rootAgent.Configuration.EnableUserRules);
                await PersistAgentConfigAsync();
                _hub.Publish("settings.changed", CurrentAgentConfig());
                return Results.Ok(new { applied = mode.Name, model });
            });

            api.MapPost("/orchestrator/new-session", () =>
            {
                _orchestrator.StartNewSession();
                return Results.Ok();
            });

            api.MapPost("/orchestrator/sessions/{id}/switch", async (string id) =>
            {
                var session = await _history.GetSessionAsync(id);
                if (session == null || session.ChatType != "main")
                {
                    return Results.NotFound(new { error = $"Chat session {id} not found" });
                }
                if (id == _orchestrator.CurrentSessionId)
                {
                    return Results.Ok(new { sessionId = id });
                }
                if (!await _orchestrator.SwitchSessionAsync(id, _history))
                {
                    return Results.Conflict(new { error = "Orchestrator is busy; cancel the current run first" });
                }
                return Results.Ok(new { sessionId = id });
            });

            api.MapPut("/sessions/{id}", async (string id, SessionRenameRequest request) =>
            {
                var title = request.Title?.Trim();
                if (string.IsNullOrEmpty(title))
                {
                    return Results.BadRequest(new { error = "Title is required" });
                }
                if (!await _history.RenameSessionAsync(id, title))
                {
                    return Results.NotFound(new { error = $"Session {id} not found" });
                }
                _hub.Publish("sessions.changed", new { sessionId = id });
                return Results.Ok();
            });

            api.MapDelete("/sessions/{id}", async (string id) =>
            {
                if (id == _orchestrator.CurrentSessionId)
                {
                    if (_orchestrator.IsBusy)
                    {
                        return Results.Conflict(new { error = "Orchestrator is busy; cancel the current run first" });
                    }
                    _orchestrator.StartNewSession();
                }
                if (!await _history.DeleteSessionAsync(id))
                {
                    return Results.NotFound(new { error = $"Session {id} not found" });
                }
                _hub.Publish("sessions.changed", new { sessionId = id });
                return Results.Ok();
            });
        }

        // ToolCallsJson stores the provider-shaped tool_call array; the UI only
        // needs the function names to label the row.
        private static string[]? ExtractToolCallNames(string? toolCallsJson)
        {
            if (string.IsNullOrEmpty(toolCallsJson)) return null;
            try
            {
                using var doc = JsonDocument.Parse(toolCallsJson);
                if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
                var names = doc.RootElement.EnumerateArray()
                    .Select(tc => tc.TryGetProperty("function", out var fn) && fn.ValueKind == JsonValueKind.Object
                        && fn.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String
                            ? name.GetString()
                            : null)
                    .Where(n => !string.IsNullOrEmpty(n))
                    .Select(n => n!)
                    .ToArray();
                return names.Length > 0 ? names : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static object ProjectTaskView(TaskView view)
        {
            var t = view.Task;
            return new
            {
                id = t.Id,
                scope = t.Scope,
                board = t.Board,
                title = t.Title,
                notes = t.Notes,
                status = t.Status,
                priority = t.Priority,
                sortOrder = t.SortOrder,
                createdBy = t.CreatedBy,
                agentAvailable = t.AgentAvailable,
                requiresApproval = t.RequiresApproval,
                userHandoffOnly = t.UserHandoffOnly,
                claimStatus = t.ClaimStatus,
                claimedBy = t.ClaimedBy,
                recurrenceKind = t.RecurrenceKind,
                recurrenceIntervalSeconds = t.RecurrenceIntervalSeconds,
                recurrenceCron = t.RecurrenceCron,
                recurrenceDescription = view.RecurrenceDescription,
                catchUpPolicy = t.CatchUpPolicy,
                nextRunAt = t.NextRunAt,
                lastRunAt = t.LastRunAt,
                createdAt = t.CreatedAt,
                updatedAt = t.UpdatedAt,
                completedAt = t.CompletedAt,
                blocked = view.Blocked,
                blockedBy = view.BlockedBy,
                dispatchedTo = view.DispatchedTo,
                hasWaiters = view.HasWaiters
            };
        }

        private object ProjectAgents()
        {
            return AgentManager.Instance.GetAgentContexts()
                .OrderBy(c => c.CreatedAt)
                .Select(c => new
                {
                    agentId = c.Id,
                    name = c.Name,
                    purpose = c.Purpose,
                    status = c.Status.ToString(),
                    model = c.Agent.Configuration.Model,
                    createdAt = c.CreatedAt,
                    runningSeconds = (DateTime.Now - c.CreatedAt).TotalSeconds,
                    currentTask = c.CurrentTask == null ? null : new
                    {
                        taskId = c.CurrentTask.Id,
                        description = c.CurrentTask.Description,
                        startedAt = c.CurrentTask.StartedAt
                    }
                })
                .ToList();
        }

        private const string ContentSecurityPolicy =
            "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; " +
            "img-src 'self' data:; font-src 'self'; connect-src 'self'; " +
            "base-uri 'none'; frame-ancestors 'none'";

        private void MapStaticAssets(WebApplication app)
        {
            var assembly = Assembly.GetExecutingAssembly();
            const string prefix = "wwwroot/";

            // Resource names come from the csproj LogicalName; RecursiveDir uses the
            // build host's separator, so normalize to forward slashes.
            var assets = assembly.GetManifestResourceNames()
                .Where(n => n.Replace('\\', '/').StartsWith(prefix, StringComparison.Ordinal))
                .ToDictionary(n => n.Replace('\\', '/')[prefix.Length..], n => n);

            app.MapGet("/", () => ServeAsset(assembly, assets, "index.html"));
            app.MapGet("/{**path}", (string path) => ServeAsset(assembly, assets, path));
        }

        private IResult ServeAsset(Assembly assembly, Dictionary<string, string> assets, string name)
        {
            if (!assets.TryGetValue(name, out var resourceName))
            {
                return Results.NotFound();
            }

            var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                return Results.NotFound();
            }

            // Vendored libraries and fonts are versioned by filename; cache them hard.
            // App assets change between builds, so let the browser revalidate.
            var cacheControl = name.StartsWith("vendor/", StringComparison.Ordinal)
                ? "public, max-age=604800, immutable"
                : "no-cache";

            if (name.EndsWith(".html", StringComparison.Ordinal))
            {
                using var reader = new StreamReader(stream, Encoding.UTF8);
                var html = reader.ReadToEnd().Replace("__SATURN_TOKEN__", _apiToken);
                return new HeaderResult(Results.Content(html, GetContentType(name)), cacheControl, ContentSecurityPolicy);
            }

            return new HeaderResult(Results.Stream(stream, GetContentType(name)), cacheControl);
        }

        private sealed class HeaderResult : IResult
        {
            private readonly IResult _inner;
            private readonly string _cacheControl;
            private readonly string? _csp;

            public HeaderResult(IResult inner, string cacheControl, string? csp = null)
            {
                _inner = inner;
                _cacheControl = cacheControl;
                _csp = csp;
            }

            public Task ExecuteAsync(HttpContext httpContext)
            {
                httpContext.Response.Headers.CacheControl = _cacheControl;
                if (_csp != null)
                {
                    httpContext.Response.Headers.ContentSecurityPolicy = _csp;
                }
                return _inner.ExecuteAsync(httpContext);
            }
        }

        private static string GetContentType(string fileName)
        {
            return Path.GetExtension(fileName).ToLowerInvariant() switch
            {
                ".html" => "text/html; charset=utf-8",
                ".css" => "text/css; charset=utf-8",
                ".js" => "text/javascript; charset=utf-8",
                ".svg" => "image/svg+xml",
                ".png" => "image/png",
                ".ico" => "image/x-icon",
                ".woff2" => "font/woff2",
                ".woff" => "font/woff",
                ".ttf" => "font/ttf",
                _ => "application/octet-stream"
            };
        }
    }
}
