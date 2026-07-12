using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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

namespace Saturn.Web
{
    public class WebServer
    {
        private readonly Agent _rootAgent;
        private readonly ILlmClientSource _clientSource;
        private readonly int _port;
        private readonly DateTime _startedAt = DateTime.UtcNow;

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

        public async Task RunAsync(CancellationToken cancellationToken = default)
        {
            CommandApprovalService.GlobalOverride = _approvalCoordinator;
            TaskSystem.Store = _tasks;
            TaskSystem.Coordinator = _coordinator;
            _tasks.OnTaskChanged += (change, task) =>
            {
                _hub.Publish("tasks.changed", new { taskId = task.Id, scope = task.Scope, board = task.Board, change });
                if (change == "completed" && TaskStatuses.IsTerminal(task.Status))
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

            MapStaticAssets(app);
            MapApi(app);

            await app.RunAsync(cancellationToken);
        }

        private void WireAgentManagerEvents()
        {
            var manager = AgentManager.Instance;
            manager.OnAgentCreated += (agentId, name) =>
                _hub.Publish("agent.created", new { agentId, name });
            manager.OnAgentStatusChanged += (agentId, name, status) =>
                _hub.Publish("agent.status", new { agentId, name, status });
            manager.OnTaskCompleted += (taskId, result) =>
                _hub.Publish("task.completed", new
                {
                    taskId,
                    result.AgentId,
                    result.AgentName,
                    result.Success,
                    result.CompletedAt,
                    durationSeconds = result.Duration.TotalSeconds
                });
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
                return Results.Ok(new
                {
                    task = ProjectTaskView(view),
                    dependents,
                    runs,
                    dispatches
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
            });

            api.MapPost("/todos/{id}/complete", async (string id, TaskCompleteRequest request) =>
            {
                var task = await _tasks.CompleteAsync(id, request.Success ?? true, request.Note);
                return task == null
                    ? Results.NotFound(new { error = $"Task {id} not found" })
                    : Results.Ok(ProjectTaskView(await _tasks.BuildViewAsync(task)));
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
                var (ok, message, _) = await _coordinator.DispatchTaskAsync(id, request.AgentId, status.Name);
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
                    m.SequenceNumber
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

        private static void MapStaticAssets(WebApplication app)
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

        private static IResult ServeAsset(Assembly assembly, Dictionary<string, string> assets, string name)
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

            return new CacheHeaderResult(Results.Stream(stream, GetContentType(name)), cacheControl);
        }

        private sealed class CacheHeaderResult : IResult
        {
            private readonly IResult _inner;
            private readonly string _cacheControl;

            public CacheHeaderResult(IResult inner, string cacheControl)
            {
                _inner = inner;
                _cacheControl = cacheControl;
            }

            public Task ExecuteAsync(HttpContext httpContext)
            {
                httpContext.Response.Headers.CacheControl = _cacheControl;
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
