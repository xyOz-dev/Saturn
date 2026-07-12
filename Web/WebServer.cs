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
using Saturn.Data;
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
        private readonly TodoStore _todos = new();
        private readonly OrchestratorService _orchestrator;
        private readonly WebCommandApprovalService _approvals;
        private readonly ChatHistoryRepository _history = new();

        public WebServer(Agent rootAgent, ILlmClientSource clientSource, int port)
        {
            _rootAgent = rootAgent;
            _clientSource = clientSource;
            _port = port;
            _orchestrator = new OrchestratorService(rootAgent, _hub);
            _approvals = new WebCommandApprovalService(_hub);
        }

        public string Url => $"http://localhost:{_port}";

        public async Task RunAsync(CancellationToken cancellationToken = default)
        {
            CommandApprovalService.GlobalOverride = _approvals;
            WireAgentManagerEvents();

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

            api.MapGet("/overview", () =>
            {
                var contexts = manager.GetAgentContexts();
                var completed = manager.GetCompletedTasks();
                var todos = _todos.GetAll();
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
                        open = todos.Count(t => t.Status != "done"),
                        done = todos.Count(t => t.Status == "done")
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

            api.MapGet("/todos", () => Results.Ok(_todos.GetAll()));

            api.MapPost("/todos", (TodoCreateRequest request) =>
            {
                if (string.IsNullOrWhiteSpace(request.Title))
                {
                    return Results.BadRequest(new { error = "title is required" });
                }
                var item = _todos.Add(request.Title, request.Notes, request.Priority);
                _hub.Publish("todos.changed");
                return Results.Ok(item);
            });

            api.MapPatch("/todos/{id}", (string id, TodoUpdateRequest request) =>
            {
                var item = _todos.Update(id, request.Title, request.Notes, request.Status, request.Priority, request.Order);
                if (item == null)
                {
                    return Results.NotFound(new { error = $"Todo {id} not found" });
                }
                _hub.Publish("todos.changed");
                return Results.Ok(item);
            });

            api.MapDelete("/todos/{id}", (string id) =>
            {
                if (!_todos.Delete(id))
                {
                    return Results.NotFound(new { error = $"Todo {id} not found" });
                }
                _hub.Publish("todos.changed");
                return Results.Ok();
            });

            api.MapPost("/todos/clear-completed", () =>
            {
                var removed = _todos.ClearCompleted();
                if (removed > 0)
                {
                    _hub.Publish("todos.changed");
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

            api.MapGet("/settings", () => Results.Ok(new
            {
                maxConcurrentAgents = manager.GetMaxConcurrentAgents(),
                requireCommandApproval = _rootAgent.Configuration.RequireCommandApproval,
                provider = _clientSource.ActiveProviderName,
                model = _rootAgent.Configuration.Model
            }));

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
                _hub.Publish("settings.changed", new
                {
                    maxConcurrentAgents = manager.GetMaxConcurrentAgents(),
                    requireCommandApproval = _rootAgent.Configuration.RequireCommandApproval
                });
                return Results.Ok(new
                {
                    maxConcurrentAgents = manager.GetMaxConcurrentAgents(),
                    requireCommandApproval = _rootAgent.Configuration.RequireCommandApproval
                });
            });
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
            const string prefix = "Saturn.Web.wwwroot.";

            var assets = assembly.GetManifestResourceNames()
                .Where(n => n.StartsWith(prefix, StringComparison.Ordinal))
                .ToDictionary(n => n[prefix.Length..], n => n);

            app.MapGet("/", () => ServeAsset(assembly, assets, "index.html"));

            foreach (var asset in assets.Keys.Where(k => k != "index.html"))
            {
                var name = asset;
                app.MapGet($"/{name}", () => ServeAsset(assembly, assets, name));
            }
        }

        private static IResult ServeAsset(Assembly assembly, Dictionary<string, string> assets, string name)
        {
            if (!assets.TryGetValue(name, out var resourceName))
            {
                return Results.NotFound();
            }

            var stream = assembly.GetManifestResourceStream(resourceName);
            return stream == null
                ? Results.NotFound()
                : Results.Stream(stream, GetContentType(name));
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
                _ => "application/octet-stream"
            };
        }
    }
}
