using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Saturn.Agents;
using Saturn.Agents.Core;
using Saturn.Agents.MultiAgent.Objects;
using Saturn.Config;
using Saturn.Data;
using Saturn.Providers;
using Saturn.OpenRouter.Models.Api.Chat;

namespace Saturn.Agents.MultiAgent
{
    public class AgentManager
    {
        private static AgentManager? _instance;
        private readonly ConcurrentDictionary<string, SubAgentContext> _runningAgents;
        private readonly ConcurrentDictionary<string, AgentTaskResult> _completedTasks;
        private readonly ConcurrentDictionary<string, ReviewerContext> _reviewers;
        private readonly SemaphoreSlim _reviewerSemaphore = new SemaphoreSlim(25);
        private ILLMClient _client = null!;
        private const int MaxConcurrentAgents = 25;
        private string? _parentSessionId;
        private bool _parentEnableUserRules = true;
        
        public static AgentManager Instance => _instance ??= new AgentManager();
        
        public event Action<string, string, string>? OnAgentStatusChanged;
        public event Action<string, string>? OnAgentCreated;
        public event Action<string, AgentTaskResult>? OnTaskCompleted;
        
        private AgentManager()
        {
            _runningAgents = new ConcurrentDictionary<string, SubAgentContext>();
            _completedTasks = new ConcurrentDictionary<string, AgentTaskResult>();
            _reviewers = new ConcurrentDictionary<string, ReviewerContext>();
        }
        
        public void Initialize(ILLMClient client)
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
        
        public void SetParentEnableUserRules(bool enableUserRules)
        {
            _parentEnableUserRules = enableUserRules;
        }
        
        public bool GetParentEnableUserRules()
        {
            return _parentEnableUserRules;
        }
        
        public async Task<(bool success, string result, List<string>? runningTaskIds)> TryCreateSubAgent(
            string name, 
            string purpose, 
            string model = "anthropic/claude-3.5-sonnet",
            bool enableTools = true,
            double? temperature = null,
            int? maxTokens = null,
            double? topP = null,
            string? systemPromptOverride = null,
            bool? includeUserRules = null)
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
                SystemPrompt = await SystemPrompt.Create(systemPrompt, includeDirectories: true, includeUserRules: includeUserRules ?? _parentEnableUserRules),
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
        
        public Task<string> HandOffTask(string agentId, string task, Dictionary<string, object>? context = null)
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
                    
                    if (SubAgentPreferences.Instance.EnableReviewStage)
                    {
                        agentContext.Status = AgentStatus.BeingReviewed;
                        OnAgentStatusChanged?.Invoke(agentId, agentContext.Name, "Being Reviewed");
                        
                        var currentResult = result.Content.ToString();
                        ReviewDecision reviewDecision = await StartReviewProcess(
                            agentId, 
                            taskId, 
                            task, 
                            currentResult,
                            agentContext);
                        
                        while (reviewDecision.Status == ReviewStatus.RevisionRequested && 
                               agentContext.RevisionCount < SubAgentPreferences.Instance.MaxRevisionCycles)
                        {
                            agentContext.Status = AgentStatus.Revising;
                            agentContext.RevisionCount++;
                            OnAgentStatusChanged?.Invoke(agentId, agentContext.Name, $"Revising (Attempt {agentContext.RevisionCount})");
                            
                            var revisionInput = $"Please revise your previous work based on this feedback:\n{reviewDecision.Feedback}\n\nOriginal task: {task}";
                            var revisedResult = await agentContext.Agent.Execute<Message>(revisionInput);
                            currentResult = revisedResult.Content.ToString();
                            
                            agentContext.Status = AgentStatus.BeingReviewed;
                            OnAgentStatusChanged?.Invoke(agentId, agentContext.Name, $"Being Reviewed (Revision {agentContext.RevisionCount})");
                            
                            reviewDecision = await StartReviewProcess(
                                agentId, 
                                taskId, 
                                task, 
                                currentResult,
                                agentContext);
                        }
                        
                        if (reviewDecision.Status == ReviewStatus.Approved)
                        {
                            var approvalMessage = agentContext.RevisionCount > 0 
                                ? $"{currentResult}\n\n[Review: Approved after {agentContext.RevisionCount} revision(s) - {reviewDecision.Feedback}]"
                                : $"{currentResult}\n\n[Review: Approved - {reviewDecision.Feedback}]";
                            CompleteTask(taskId, agentId, agentContext, true, approvalMessage);
                        }
                        else if (reviewDecision.Status == ReviewStatus.Rejected)
                        {
                            CompleteTask(taskId, agentId, agentContext, false, 
                                $"Task rejected by reviewer: {reviewDecision.Feedback}");
                        }
                        else
                        {
                            CompleteTask(taskId, agentId, agentContext, false, 
                                $"Task failed review after {agentContext.RevisionCount} revision(s). Final feedback: {reviewDecision.Feedback}");
                        }
                    }
                    else
                    {
                        CompleteTask(taskId, agentId, agentContext, true, result.Content.ToString());
                    }
                }
                catch (Exception ex)
                {
                    CompleteTask(taskId, agentId, agentContext, false, $"Error: {ex.Message}");
                }
            });
            
            return Task.FromResult(taskId);
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
            
            var statusText = context.Status.ToString();
            if (context.Status == AgentStatus.BeingReviewed)
            {
                statusText = "Being Reviewed";
            }
            else if (context.Status == AgentStatus.Revising)
            {
                statusText = $"Revising (Attempt {context.RevisionCount})";
            }
            
            return new AgentStatusInfo
            {
                AgentId = agentId,
                Name = context.Name,
                Status = statusText,
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
            return _runningAgents.Values.Select(context => 
            {
                var statusText = context.Status.ToString();
                if (context.Status == AgentStatus.BeingReviewed)
                {
                    statusText = "Being Reviewed";
                }
                else if (context.Status == AgentStatus.Revising)
                {
                    statusText = $"Revising (Attempt {context.RevisionCount})";
                }
                
                return new AgentStatusInfo
                {
                    AgentId = context.Id,
                    Name = context.Name,
                    Status = statusText,
                    CurrentTask = context.CurrentTask?.Description,
                    TaskId = context.CurrentTask?.Id,
                    IsIdle = context.Status == AgentStatus.Idle,
                    Exists = true,
                    RunningTime = context.CurrentTask != null ? 
                        DateTime.Now - context.CurrentTask.StartedAt : TimeSpan.Zero
                };
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
        
        private async Task<ReviewDecision> StartReviewProcess(
            string subAgentId, 
            string taskId, 
            string originalTask, 
            string subAgentResult,
            SubAgentContext subAgentContext)
        {
            await _reviewerSemaphore.WaitAsync();
            try
            {
                var reviewerId = $"reviewer_{Guid.NewGuid():N}".Substring(0, 16);
                var prefs = SubAgentPreferences.Instance;
                
                var parentContext = "";
                if (_parentSessionId != null && subAgentContext.Agent.ChatHistory.Count > 0)
                {
                    var recentMessages = subAgentContext.Agent.ChatHistory
                        .TakeLast(Math.Min(5, subAgentContext.Agent.ChatHistory.Count))
                        .Select(m => $"{m.Role}: {(m.Content.ValueKind == JsonValueKind.String ? m.Content.GetString() : m.Content.GetRawText())}")
                        .ToList();
                    parentContext = string.Join("\n", recentMessages);
                }
                
                var reviewPrompt = $@"You are a quality assurance reviewer for sub-agent tasks.

CONTEXT FROM PARENT CONVERSATION:
{parentContext}

ORIGINAL TASK GIVEN TO SUB-AGENT:
{originalTask}

SUB-AGENT NAME: {subAgentContext.Name}
SUB-AGENT PURPOSE: {subAgentContext.Purpose}

SUB-AGENT'S RESULT:
{subAgentResult}

REVIEW CRITERIA:
1. Did the sub-agent complete the requested task correctly?
2. Does the work align with the parent conversation's intent?
3. Are there any errors, missing steps, or deviations from requirements?
4. Is the quality of work acceptable?

DECISION REQUIRED:
- If the work is satisfactory, respond with: APPROVED: [brief reason]
- If specific improvements are needed, respond with: REVISION: [specific feedback and requirements]
- If fundamentally wrong, respond with: REJECTED: [clear reason]

Your decision:";

                var reviewerConfig = new AgentConfiguration
                {
                    Name = $"Reviewer for {subAgentContext.Name}",
                    SystemPrompt = await SystemPrompt.Create("You are a specialized quality assurance reviewer. Be thorough but fair in your assessments.", includeDirectories: true, includeUserRules: false),
                    Client = _client,
                    Model = prefs.ReviewerModel,
                    Temperature = 0.2,
                    MaxTokens = 2048,
                    TopP = 0.95,
                    EnableTools = false,
                    MaintainHistory = false
                };
                
                var reviewerAgent = new Agent(reviewerConfig);
                
                var reviewerContext = new ReviewerContext
                {
                    Id = reviewerId,
                    ReviewerAgent = reviewerAgent,
                    SubAgentId = subAgentId,
                    TaskId = taskId,
                    StartedAt = DateTime.Now
                };
                
                _reviewers[reviewerId] = reviewerContext;
                
                var reviewTask = reviewerAgent.Execute<Message>(reviewPrompt);
                var timeoutTask = Task.Delay(prefs.ReviewTimeoutSeconds * 1000);
                
                var completedTask = await Task.WhenAny(reviewTask, timeoutTask);
                
                if (completedTask == timeoutTask)
                {
                    _reviewers.TryRemove(reviewerId, out _);
                    return new ReviewDecision
                    {
                        Status = ReviewStatus.Approved,
                        Feedback = "Review timed out - auto-approved"
                    };
                }
                
                var reviewResult = await reviewTask;
                var reviewText = reviewResult.Content.ToString().Trim();
                
                _reviewers.TryRemove(reviewerId, out _);
                
                // Parse review decision
                if (reviewText.StartsWith("APPROVED:", StringComparison.OrdinalIgnoreCase))
                {
                    return new ReviewDecision
                    {
                        Status = ReviewStatus.Approved,
                        Feedback = reviewText.Substring(9).Trim()
                    };
                }
                else if (reviewText.StartsWith("REVISION:", StringComparison.OrdinalIgnoreCase))
                {
                    return new ReviewDecision
                    {
                        Status = ReviewStatus.RevisionRequested,
                        Feedback = reviewText.Substring(9).Trim()
                    };
                }
                else if (reviewText.StartsWith("REJECTED:", StringComparison.OrdinalIgnoreCase))
                {
                    return new ReviewDecision
                    {
                        Status = ReviewStatus.Rejected,
                        Feedback = reviewText.Substring(9).Trim()
                    };
                }
                else
                {
                    // Default to approved if can't parse
                    return new ReviewDecision
                    {
                        Status = ReviewStatus.Approved,
                        Feedback = "Review response unclear - defaulting to approved"
                    };
                }
            }
            finally
            {
                _reviewerSemaphore.Release();
            }
        }
        
        private void CompleteTask(string taskId, string agentId, SubAgentContext agentContext, bool success, string result)
        {
            var taskResult = new AgentTaskResult
            {
                TaskId = taskId,
                AgentId = agentId,
                AgentName = agentContext.Name,
                Success = success,
                Result = result,
                CompletedAt = DateTime.Now,
                Duration = DateTime.Now - agentContext.CurrentTask!.StartedAt
            };
            
            _completedTasks[taskId] = taskResult;
            agentContext.CurrentTask!.Status = success ? TaskStatus.Completed : TaskStatus.Failed;
            agentContext.Status = AgentStatus.Idle;
            agentContext.CurrentTask = null;
            agentContext.RevisionCount = 0;
            
            OnTaskCompleted?.Invoke(taskId, taskResult);
            OnAgentStatusChanged?.Invoke(agentId, agentContext.Name, "Idle");
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
}
