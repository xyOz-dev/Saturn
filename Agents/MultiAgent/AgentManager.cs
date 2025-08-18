using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Saturn.Agents;
using Saturn.Agents.Core;
using Saturn.Data;
using Saturn.OpenRouter;
using Saturn.OpenRouter.Models.Api.Chat;

namespace Saturn.Agents.MultiAgent
{
    public class AgentManager
    {
        private static AgentManager? _instance;
        private readonly ConcurrentDictionary<string, SubAgentContext> _runningAgents;
        private readonly ConcurrentDictionary<string, AgentTaskResult> _completedTasks;
        private OpenRouterClient _client = null!;
        private const int MaxConcurrentAgents = 25;
        private string? _parentSessionId;
        
        public static AgentManager Instance => _instance ??= new AgentManager();
        
        public event Action<string, string, string>? OnAgentStatusChanged;
        public event Action<string, string>? OnAgentCreated;
        public event Action<string, AgentTaskResult>? OnTaskCompleted;
        
        private AgentManager()
        {
            _runningAgents = new ConcurrentDictionary<string, SubAgentContext>();
            _completedTasks = new ConcurrentDictionary<string, AgentTaskResult>();
        }
        
        public void Initialize(OpenRouterClient client)
        {
            if (_instance != null)
            {
                _instance._client = client;
            }
        }
        
        public void SetParentSessionId(string? sessionId)
        {
            _parentSessionId = sessionId;
        }
        
        public async Task<(bool success, string result, List<string>? runningTaskIds)> TryCreateSubAgent(
            string name, 
            string purpose, 
            string model = "anthropic/claude-3.5-sonnet",
            bool enableTools = true,
            double? temperature = null,
            int? maxTokens = null,
            double? topP = null,
            string? systemPromptOverride = null)
        {
            if (_runningAgents.Count >= MaxConcurrentAgents)
            {
                var runningTasks = _runningAgents
                    .Where(kvp => kvp.Value.CurrentTask != null)
                    .Select(kvp => kvp.Value.CurrentTask!.Id)
                    .ToList();
                
                return (false, $"Maximum concurrent agent limit ({MaxConcurrentAgents}) reached", runningTasks);
            }
            
            var agentId = $"agent_{Guid.NewGuid():N}".Substring(0, 12);
            
            var systemPrompt = systemPromptOverride ?? $@"You are a specialized sub-agent named {name}.
Your purpose: {purpose}
You work as part of a larger system and should focus on your specific task.
Report your progress clearly and concisely.";
            
            var config = new AgentConfiguration
            {
                Name = name,
                SystemPrompt = await SystemPrompt.Create(systemPrompt),
                Client = _client,
                Model = model,
                Temperature = temperature ?? 0.3,
                MaxTokens = maxTokens ?? 4096,
                TopP = topP ?? 0.95,
                EnableTools = enableTools,
                MaintainHistory = true,
                MaxHistoryMessages = 20
            };
            
            var agent = new Agent(config);
            
            if (_parentSessionId != null)
            {
                await agent.InitializeSessionAsync("agent", _parentSessionId);
            }
            
            var context = new SubAgentContext
            {
                Id = agentId,
                Agent = agent,
                Name = name,
                Purpose = purpose,
                Status = AgentStatus.Idle,
                CreatedAt = DateTime.Now,
                CurrentTask = null
            };
            
            _runningAgents[agentId] = context;
            OnAgentCreated?.Invoke(agentId, name);
            OnAgentStatusChanged?.Invoke(agentId, name, "Idle");
            
            return (true, agentId, null);
        }
        
        public async Task<string> HandOffTask(string agentId, string task, Dictionary<string, object>? context = null)
        {
            if (!_runningAgents.TryGetValue(agentId, out var agentContext))
            {
                throw new InvalidOperationException($"Agent {agentId} not found");
            }
            
            var taskId = $"task_{Guid.NewGuid():N}".Substring(0, 12);
            
            agentContext.Status = AgentStatus.Working;
            agentContext.CurrentTask = new AgentTask
            {
                Id = taskId,
                Description = task,
                Context = context,
                StartedAt = DateTime.Now,
                Status = TaskStatus.Running
            };
            
            OnAgentStatusChanged?.Invoke(agentId, agentContext.Name, "Working");
            
            _ = Task.Run(async () =>
            {
                try
                {
                    var input = task;
                    if (context != null && context.Any())
                    {
                        input += "\n\nContext:\n";
                        foreach (var kvp in context)
                        {
                            input += $"- {kvp.Key}: {kvp.Value}\n";
                        }
                    }
                    
                    var result = await agentContext.Agent.Execute<Message>(input);
                    
                    var taskResult = new AgentTaskResult
                    {
                        TaskId = taskId,
                        AgentId = agentId,
                        AgentName = agentContext.Name,
                        Success = true,
                        Result = result.Content.ToString(),
                        CompletedAt = DateTime.Now,
                        Duration = DateTime.Now - agentContext.CurrentTask.StartedAt
                    };
                    
                    _completedTasks[taskId] = taskResult;
                    agentContext.CurrentTask.Status = TaskStatus.Completed;
                    agentContext.Status = AgentStatus.Idle;
                    agentContext.CurrentTask = null;
                    
                    OnTaskCompleted?.Invoke(taskId, taskResult);
                    OnAgentStatusChanged?.Invoke(agentId, agentContext.Name, "Idle");
                }
                catch (Exception ex)
                {
                    var taskResult = new AgentTaskResult
                    {
                        TaskId = taskId,
                        AgentId = agentId,
                        AgentName = agentContext.Name,
                        Success = false,
                        Result = $"Error: {ex.Message}",
                        CompletedAt = DateTime.Now,
                        Duration = DateTime.Now - agentContext.CurrentTask!.StartedAt
                    };
                    
                    _completedTasks[taskId] = taskResult;
                    agentContext.CurrentTask!.Status = TaskStatus.Failed;
                    agentContext.Status = AgentStatus.Error;
                    
                    OnTaskCompleted?.Invoke(taskId, taskResult);
                    OnAgentStatusChanged?.Invoke(agentId, agentContext.Name, $"Error: {ex.Message}");
                }
            });
            
            return taskId;
        }
        
        public AgentStatusInfo GetAgentStatus(string agentId)
        {
            if (!_runningAgents.TryGetValue(agentId, out var context))
            {
                return new AgentStatusInfo
                {
                    AgentId = agentId,
                    Exists = false
                };
            }
            
            return new AgentStatusInfo
            {
                AgentId = agentId,
                Name = context.Name,
                Status = context.Status.ToString(),
                CurrentTask = context.CurrentTask?.Description,
                TaskId = context.CurrentTask?.Id,
                IsIdle = context.Status == AgentStatus.Idle,
                Exists = true,
                RunningTime = context.CurrentTask != null ? 
                    DateTime.Now - context.CurrentTask.StartedAt : TimeSpan.Zero
            };
        }
        
        public List<AgentStatusInfo> GetAllAgentStatuses()
        {
            return _runningAgents.Values.Select(context => new AgentStatusInfo
            {
                AgentId = context.Id,
                Name = context.Name,
                Status = context.Status.ToString(),
                CurrentTask = context.CurrentTask?.Description,
                TaskId = context.CurrentTask?.Id,
                IsIdle = context.Status == AgentStatus.Idle,
                Exists = true,
                RunningTime = context.CurrentTask != null ? 
                    DateTime.Now - context.CurrentTask.StartedAt : TimeSpan.Zero
            }).ToList();
        }
        
        public AgentTaskResult? GetTaskResult(string taskId)
        {
            return _completedTasks.TryGetValue(taskId, out var result) ? result : null;
        }
        
        public async Task<bool> WaitForTask(string taskId, int timeoutMs = 30000)
        {
            if (_completedTasks.ContainsKey(taskId))
            {
                return true;
            }
            
            var startTime = DateTime.Now;
            while ((DateTime.Now - startTime).TotalMilliseconds < timeoutMs)
            {
                await Task.Delay(100);
                if (_completedTasks.ContainsKey(taskId))
                {
                    return true;
                }
            }
            return false;
        }
        
        public async Task<List<AgentTaskResult>> WaitForAllTasks(List<string> taskIds, int timeoutMs = 60000)
        {
            var results = new List<AgentTaskResult>();
            var pendingTaskIds = new List<string>();
            
            // First check which tasks are already complete
            foreach (var taskId in taskIds)
            {
                if (_completedTasks.TryGetValue(taskId, out var result))
                {
                    results.Add(result);
                }
                else
                {
                    pendingTaskIds.Add(taskId);
                }
            }
            
            if (pendingTaskIds.Count == 0)
            {
                return results;
            }
            
            var startTime = DateTime.Now;
            while (pendingTaskIds.Count > 0 && (DateTime.Now - startTime).TotalMilliseconds < timeoutMs)
            {
                await Task.Delay(100);
                
                for (int i = pendingTaskIds.Count - 1; i >= 0; i--)
                {
                    if (_completedTasks.TryGetValue(pendingTaskIds[i], out var result))
                    {
                        results.Add(result);
                        pendingTaskIds.RemoveAt(i);
                    }
                }
            }
            
            return results;
        }
        
        public void TerminateAgent(string agentId)
        {
            if (_runningAgents.TryRemove(agentId, out var context))
            {
                context.Status = AgentStatus.Terminated;
                OnAgentStatusChanged?.Invoke(agentId, context.Name, "Terminated");
            }
        }
        
        public void ClearCompletedTasks()
        {
            _completedTasks.Clear();
        }
        
        public void TerminateAllAgents()
        {
            var agentIds = _runningAgents.Keys.ToList();
            foreach (var agentId in agentIds)
            {
                TerminateAgent(agentId);
            }
            _completedTasks.Clear();
        }
        
        public int GetCurrentAgentCount()
        {
            return _runningAgents.Count;
        }
        
        public int GetMaxConcurrentAgents()
        {
            return MaxConcurrentAgents;
        }
    }
    
    public class SubAgentContext
    {
        public string Id { get; set; } = "";
        public Agent Agent { get; set; } = null!;
        public string Name { get; set; } = "";
        public string Purpose { get; set; } = "";
        public AgentStatus Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public AgentTask? CurrentTask { get; set; }
    }
    
    public class AgentTask
    {
        public string Id { get; set; } = "";
        public string Description { get; set; } = "";
        public Dictionary<string, object>? Context { get; set; }
        public DateTime StartedAt { get; set; }
        public TaskStatus Status { get; set; }
    }
    
    public class AgentTaskResult
    {
        public string TaskId { get; set; } = "";
        public string AgentId { get; set; } = "";
        public string AgentName { get; set; } = "";
        public bool Success { get; set; }
        public string Result { get; set; } = "";
        public DateTime CompletedAt { get; set; }
        public TimeSpan Duration { get; set; }
    }
    
    public class AgentStatusInfo
    {
        public string AgentId { get; set; } = "";
        public string Name { get; set; } = "";
        public string Status { get; set; } = "";
        public string? CurrentTask { get; set; }
        public string? TaskId { get; set; }
        public bool IsIdle { get; set; }
        public bool Exists { get; set; }
        public TimeSpan RunningTime { get; set; }
    }
    
    public enum AgentStatus
    {
        Idle,
        Working,
        Error,
        Terminated
    }
    
    public enum TaskStatus
    {
        Pending,
        Running,
        Completed,
        Failed
    }
}