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
using Saturn.OpenRouter.Models.Api.Chat;
using Saturn.Providers;
using Saturn.Tools.Core;

namespace Saturn.Agents.MultiAgent
{
    public class AgentManager
    {
        private static readonly Lazy<AgentManager> _lazyInstance = new Lazy<AgentManager>(() => new AgentManager());
        private readonly ConcurrentDictionary<string, SubAgentContext> _runningAgents;
        private readonly ConcurrentDictionary<string, AgentTaskResult> _completedTasks;
        private readonly ConcurrentDictionary<string, ReviewerContext> _reviewers;
        private readonly SemaphoreSlim _reviewerSemaphore = new SemaphoreSlim(25);
        private ILlmClientSource _clientSource = null!;
        private const int DefaultMaxConcurrentAgents = 50;
        private const int AbsoluteMaxConcurrentAgents = 200;

        // Agent-lifecycle and orchestrator-only tools; sub-agents must not see these
        // (recursive create_agent, terminating siblings, or waiting on their own task).
        private static readonly HashSet<string> SubAgentExcludedTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "spawn_agent", "create_agent", "hand_off_to_agent", "terminate_agent",
            "wait_for_agent", "get_agent_status", "get_task_result",
            "claim_task", "dispatch_task", "list_due_tasks"
        };
        private int _maxConcurrentAgents = DefaultMaxConcurrentAgents;
        private readonly object _agentRegistrationLock = new object();
        private string? _parentSessionId;
        private string? _parentModel;
        private bool _parentEnableUserRules = true;
        
        public static AgentManager Instance => _lazyInstance.Value;
        
        public event Action<string, string, string>? OnAgentStatusChanged;
        public event Action<string, string>? OnAgentCreated;
        public event Action<string, AgentTaskResult>? OnTaskCompleted;
        
        private AgentManager()
        {
            _runningAgents = new ConcurrentDictionary<string, SubAgentContext>();
            _completedTasks = new ConcurrentDictionary<string, AgentTaskResult>();
            _reviewers = new ConcurrentDictionary<string, ReviewerContext>();
        }
        
        public void Initialize(ILlmClientSource clientSource)
        {
            _clientSource = clientSource;
        }
        
        public void SetParentSessionId(string? sessionId)
        {
            _parentSessionId = sessionId;
        }

        public void SetParentModel(string? model)
        {
            _parentModel = model;
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
            bool? includeUserRules = null,
            bool disposeOnTaskCompletion = false,
            IReadOnlyList<string>? allowedTools = null,
            string? systemPromptAddendum = null)
        {
            var agentId = $"agent_{Guid.NewGuid():N}".Substring(0, 12);

            var context = new SubAgentContext
            {
                Id = agentId,
                Name = name,
                Purpose = purpose,
                Status = AgentStatus.Idle,
                CreatedAt = DateTime.Now,
                CurrentTask = null,
                DisposeOnTaskCompletion = disposeOnTaskCompletion
            };

            // Reserve the slot atomically so a burst of concurrent creates can't exceed the cap.
            lock (_agentRegistrationLock)
            {
                if (_runningAgents.Count >= _maxConcurrentAgents)
                {
                    var runningTasks = _runningAgents
                        .Where(kvp => kvp.Value.CurrentTask != null)
                        .Select(kvp => kvp.Value.CurrentTask!.Id)
                        .ToList();

                    return (false, $"Maximum concurrent agent limit ({_maxConcurrentAgents}) reached", runningTasks);
                }

                _runningAgents[agentId] = context;
            }

            try
            {
                model = await ModelCatalog.ResolveModelAsync(_clientSource, model, _parentModel);

                // Clamp to the model's advertised output limit so a generous default
                // cannot turn into a 400 on models with smaller completion caps.
                var effectiveMaxTokens = maxTokens ?? SubAgentPreferences.Instance.DefaultMaxTokens;
                var modelMaxCompletion = await ModelCatalog.GetMaxCompletionTokensAsync(_clientSource, model);
                if (modelMaxCompletion.HasValue && modelMaxCompletion.Value > 0)
                {
                    effectiveMaxTokens = Math.Min(effectiveMaxTokens, modelMaxCompletion.Value);
                }

                var systemPrompt = systemPromptOverride ?? $@"You are a specialized sub-agent named {name}.
Your purpose: {purpose}

You work as part of a larger multi-agent system. Focus only on the task you are given; do not expand its scope.
Use the provided tools to do the work - read files before editing them and never assume file contents.
When the task is complete, report back concisely:
- What you did and how you verified it
- Files you created or changed, if any (list the paths)
- Anything you could not complete, and why
Your report is consumed by an orchestrator agent, so keep it factual and free of filler.";

                if (systemPromptOverride == null && !string.IsNullOrWhiteSpace(systemPromptAddendum))
                {
                    systemPrompt += $"\n\n{systemPromptAddendum}";
                }

                var subAgentTools = ToolRegistry.Instance.GetAllNames()
                    .Where(toolName => !SubAgentExcludedTools.Contains(toolName));
                if (allowedTools != null)
                {
                    subAgentTools = subAgentTools
                        .Where(toolName => allowedTools.Contains(toolName, StringComparer.OrdinalIgnoreCase));
                }

                var config = new AgentConfiguration
                {
                    Name = name,
                    SystemPrompt = await SystemPrompt.Create(systemPrompt, includeDirectories: true, includeUserRules: includeUserRules ?? _parentEnableUserRules),
                    ClientSource = _clientSource,
                    Model = model,
                    Temperature = temperature ?? 0.3,
                    MaxTokens = effectiveMaxTokens,
                    TopP = topP ?? 0.95,
                    EnableTools = enableTools,
                    ToolNames = subAgentTools.ToList(),
                    MaintainHistory = true,
                    // A real coding task is routinely 25+ tool round-trips; a low cap
                    // makes the agent forget its own task mid-way.
                    MaxHistoryMessages = 200
                };

                var agent = new Agent(config);
                agent.ManagerAgentId = agentId;

                if (_parentSessionId != null)
                {
                    await agent.InitializeSessionAsync("agent", _parentSessionId);
                }

                context.Agent = agent;
            }
            catch
            {
                _runningAgents.TryRemove(agentId, out _);
                throw;
            }

            OnAgentCreated?.Invoke(agentId, name);
            OnAgentStatusChanged?.Invoke(agentId, name, "Idle");

            return (true, agentId, null);
        }
        
        public async Task<string> HandOffTask(string agentId, string task, Dictionary<string, object>? context = null, Func<string, Task>? onBeforeStart = null)
        {
            if (!_runningAgents.TryGetValue(agentId, out var agentContext))
            {
                throw new InvalidOperationException($"Agent {agentId} not found");
            }

            var taskId = $"task_{Guid.NewGuid():N}".Substring(0, 12);
            var cancellation = new CancellationTokenSource();

            lock (_agentRegistrationLock)
            {
                if (agentContext.Agent == null)
                {
                    throw new InvalidOperationException($"Agent {agentId} is still initializing; retry shortly");
                }

                if (agentContext.Status != AgentStatus.Idle)
                {
                    throw new InvalidOperationException(
                        $"Agent {agentId} is busy (status: {agentContext.Status}, task: {agentContext.CurrentTask?.Id ?? "unknown"}); wait for it to finish before handing off another task");
                }

                agentContext.Status = AgentStatus.Working;
                agentContext.Cancellation = cancellation;
                agentContext.CurrentTask = new AgentTask
                {
                    Id = taskId,
                    Description = task,
                    Context = context,
                    StartedAt = DateTime.Now,
                    Status = TaskStatus.Running
                };
            }

            OnAgentStatusChanged?.Invoke(agentId, agentContext.Name, "Working");

            // Callers persist bookkeeping keyed on the task id here, before the
            // agent runs; a fast-completing agent would otherwise race past it.
            if (onBeforeStart != null)
            {
                try
                {
                    await onBeforeStart(taskId);
                }
                catch
                {
                    var rolledBackToIdle = false;
                    lock (_agentRegistrationLock)
                    {
                        // TerminateAgent may have run while the callback was awaited;
                        // never resurrect a terminated agent, and only roll back the
                        // task this handoff created.
                        if (agentContext.Status != AgentStatus.Terminated &&
                            agentContext.CurrentTask?.Id == taskId)
                        {
                            agentContext.Status = AgentStatus.Idle;
                            agentContext.CurrentTask = null;
                            agentContext.Cancellation = null;
                            rolledBackToIdle = true;
                        }
                    }
                    cancellation.Dispose();
                    if (rolledBackToIdle)
                    {
                        OnAgentStatusChanged?.Invoke(agentId, agentContext.Name, "Idle");
                    }
                    throw;
                }
            }

            var executionTask = Task.Run(async () =>
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
                    
                    var result = await agentContext.Agent.Execute<Message>(input, cancellation.Token);
                    
                    if (SubAgentPreferences.Instance.EnableReviewStage)
                    {
                        bool terminated;
                        lock (_agentRegistrationLock)
                        {
                            terminated = agentContext.Status == AgentStatus.Terminated;
                            if (!terminated)
                            {
                                agentContext.Status = AgentStatus.BeingReviewed;
                            }
                        }

                        if (terminated)
                        {
                            CompleteTask(taskId, agentId, agentContext, false, "Task terminated before completion");
                            return;
                        }

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
                            lock (_agentRegistrationLock)
                            {
                                terminated = agentContext.Status == AgentStatus.Terminated;
                                if (!terminated)
                                {
                                    agentContext.Status = AgentStatus.Revising;
                                    agentContext.RevisionCount++;
                                }
                            }

                            if (terminated)
                            {
                                break;
                            }

                            OnAgentStatusChanged?.Invoke(agentId, agentContext.Name, $"Revising (Attempt {agentContext.RevisionCount})");

                            var revisionInput = $"Please revise your previous work based on this feedback:\n{reviewDecision.Feedback}\n\nOriginal task: {task}";
                            var revisedResult = await agentContext.Agent.Execute<Message>(revisionInput, cancellation.Token);
                            currentResult = revisedResult.Content.ToString();

                            lock (_agentRegistrationLock)
                            {
                                terminated = agentContext.Status == AgentStatus.Terminated;
                                if (!terminated)
                                {
                                    agentContext.Status = AgentStatus.BeingReviewed;
                                }
                            }

                            if (terminated)
                            {
                                break;
                            }

                            OnAgentStatusChanged?.Invoke(agentId, agentContext.Name, $"Being Reviewed (Revision {agentContext.RevisionCount})");

                            reviewDecision = await StartReviewProcess(
                                agentId,
                                taskId,
                                task,
                                currentResult,
                                agentContext);
                        }

                        if (terminated)
                        {
                            CompleteTask(taskId, agentId, agentContext, false, "Task terminated before completion");
                        }
                        else if (reviewDecision.Status == ReviewStatus.Approved)
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
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                    CompleteTask(taskId, agentId, agentContext, false, "Task terminated before completion");
                }
                catch (Exception ex)
                {
                    CompleteTask(taskId, agentId, agentContext, false, $"Error: {ex.Message}");
                }
            });
            agentContext.ExecutionTask = executionTask;
            _ = executionTask;

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

                var reviewerModel = await ModelCatalog.ResolveModelAsync(_clientSource, prefs.ReviewerModel, _parentModel);

                var reviewerConfig = new AgentConfiguration
                {
                    Name = $"Reviewer for {subAgentContext.Name}",
                    SystemPrompt = await SystemPrompt.Create("You are a specialized quality assurance reviewer. Be thorough but fair in your assessments.", includeDirectories: true, includeUserRules: false),
                    ClientSource = _clientSource,
                    Model = reviewerModel,
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

                var reviewCts = new CancellationTokenSource();
                var reviewTask = reviewerAgent.Execute<Message>(reviewPrompt, reviewCts.Token);
                var timeoutTask = Task.Delay(prefs.ReviewTimeoutSeconds * 1000);

                var completedTask = await Task.WhenAny(reviewTask, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    _reviewers.TryRemove(reviewerId, out _);
                    reviewCts.Cancel();
                    // The review call may still be mid-flight (e.g. blocked on a
                    // provider response); disposing the agent here could tear down
                    // state it is still touching, so defer disposal until the task
                    // actually unwinds.
                    _ = reviewTask.ContinueWith(_ =>
                    {
                        reviewerAgent.Dispose();
                        reviewCts.Dispose();
                    }, TaskScheduler.Default);

                    return new ReviewDecision
                    {
                        Status = ReviewStatus.Approved,
                        Feedback = "Reviewer timed out - result is UNREVIEWED"
                    };
                }

                try
                {
                    var reviewResult = await reviewTask;
                    var reviewText = reviewResult.Content.ToString().Trim();

                    _reviewers.TryRemove(reviewerId, out _);

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
                        return new ReviewDecision
                        {
                            Status = ReviewStatus.Approved,
                            Feedback = "Review response unclear - defaulting to approved"
                        };
                    }
                }
                finally
                {
                    reviewerAgent.Dispose();
                    reviewCts.Dispose();
                }
            }
            finally
            {
                _reviewerSemaphore.Release();
            }
        }
        
        private void CompleteTask(string taskId, string agentId, SubAgentContext agentContext, bool success, string result)
        {
            DateTime? startedAt = null;
            var becameIdle = false;

            lock (_agentRegistrationLock)
            {
                var currentTask = agentContext.CurrentTask;
                if (currentTask != null && currentTask.Id == taskId)
                {
                    startedAt = currentTask.StartedAt;
                    currentTask.Status = success ? TaskStatus.Completed : TaskStatus.Failed;
                    agentContext.CurrentTask = null;
                    agentContext.RevisionCount = 0;
                    agentContext.Cancellation?.Dispose();
                    agentContext.Cancellation = null;

                    if (agentContext.Status != AgentStatus.Terminated)
                    {
                        agentContext.Status = AgentStatus.Idle;
                        becameIdle = _runningAgents.ContainsKey(agentId);
                    }
                }
            }

            var taskResult = new AgentTaskResult
            {
                TaskId = taskId,
                AgentId = agentId,
                AgentName = agentContext.Name,
                Success = success,
                Result = result,
                CompletedAt = DateTime.Now,
                Duration = startedAt.HasValue ? DateTime.Now - startedAt.Value : TimeSpan.Zero
            };

            // Always record the result, even for a terminated or superseded task,
            // so callers waiting on the task id resolve instead of timing out.
            _completedTasks[taskId] = taskResult;

            // Publish the idle transition before completion handlers run: a handler
            // may immediately hand off another task, and its "Working" event must
            // not be followed by a stale "Idle". One-shot agents (spawn_agent) are
            // terminated instead, after their result is recorded, so they release
            // their concurrency slot without orchestrator cleanup.
            if (becameIdle && agentContext.DisposeOnTaskCompletion)
            {
                TerminateAgent(agentId);
            }
            else if (becameIdle)
            {
                OnAgentStatusChanged?.Invoke(agentId, agentContext.Name, "Idle");
            }
            OnTaskCompleted?.Invoke(taskId, taskResult);
        }

        public void TerminateAgent(string agentId)
        {
            if (_runningAgents.TryRemove(agentId, out var context))
            {
                CancellationTokenSource? cancellation;
                Task? executionTask;
                lock (_agentRegistrationLock)
                {
                    context.Status = AgentStatus.Terminated;
                    cancellation = context.Cancellation;
                    executionTask = context.ExecutionTask;
                }

                try
                {
                    cancellation?.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }

                if (context.Agent != null)
                {
                    var agentToDispose = context.Agent;
                    if (executionTask != null && !executionTask.IsCompleted)
                    {
                        // The handoff's execution loop may still be mid-flight (e.g.
                        // blocked on a provider response); disposing here could tear
                        // down state it is still touching, so defer disposal until
                        // the task actually unwinds — same reasoning as the reviewer
                        // timeout path above.
                        _ = executionTask.ContinueWith(_ =>
                        {
                            try { agentToDispose.Dispose(); } catch { }
                        }, TaskScheduler.Default);
                    }
                    else
                    {
                        try { agentToDispose.Dispose(); } catch { }
                    }
                }

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
            return _maxConcurrentAgents;
        }

        public void SetMaxConcurrentAgents(int value)
        {
            _maxConcurrentAgents = Math.Clamp(value, 1, AbsoluteMaxConcurrentAgents);
        }

        public IReadOnlyList<SubAgentContext> GetAgentContexts()
        {
            return _runningAgents.Values.ToList();
        }

        public IReadOnlyList<AgentTaskResult> GetCompletedTasks()
        {
            return _completedTasks.Values.ToList();
        }
    }
}
