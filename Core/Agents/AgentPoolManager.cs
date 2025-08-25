using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Saturn.Agents;
using Saturn.Agents.Core;
using Saturn.Core.Sessions;
using Saturn.OpenRouter;

namespace Saturn.Core.Agents
{
    public class AgentPoolManager : IDisposable
    {
        private readonly ConcurrentDictionary<string, AgentPoolEntry> _agentPool = new();
        private readonly ConcurrentDictionary<string, string> _sessionAgentMapping = new();
        private readonly OpenRouterClient _client;
        private readonly Timer _cleanupTimer;
        private readonly AgentPoolConfiguration _configuration;
        
        private static readonly Lazy<AgentPoolManager> _instance = new(() => 
            new AgentPoolManager(new OpenRouterClient(new OpenRouterOptions())));
        
        public static AgentPoolManager Instance => _instance.Value;
        
        public event EventHandler<AgentPoolEventArgs>? AgentCreated;
        public event EventHandler<AgentPoolEventArgs>? AgentRecycled;
        public event EventHandler<AgentPoolEventArgs>? AgentDestroyed;
        
        public AgentPoolManager(OpenRouterClient client, AgentPoolConfiguration? configuration = null)
        {
            _client = client;
            _configuration = configuration ?? new AgentPoolConfiguration();
            _cleanupTimer = new Timer(CleanupIdleAgents, null, 
                _configuration.CleanupInterval, _configuration.CleanupInterval);
        }
        
        public async Task<Agent> GetOrCreateAgentAsync(
            string sessionId,
            AgentConfiguration configuration,
            bool reuseExisting = true)
        {
            if (reuseExisting && _sessionAgentMapping.TryGetValue(sessionId, out var existingAgentId))
            {
                if (_agentPool.TryGetValue(existingAgentId, out var existingEntry))
                {
                    existingEntry.LastAccessTime = DateTime.UtcNow;
                    existingEntry.AccessCount++;
                    return existingEntry.Agent;
                }
            }
            
            var recycledAgent = reuseExisting ? TryRecycleAgent(configuration) : null;
            if (recycledAgent != null)
            {
                AssignAgentToSession(recycledAgent.Id, sessionId);
                AgentRecycled?.Invoke(this, new AgentPoolEventArgs 
                { 
                    Agent = recycledAgent, 
                    SessionId = sessionId 
                });
                return recycledAgent;
            }
            
            if (_agentPool.Count >= _configuration.MaxPoolSize)
            {
                await EvictLeastRecentlyUsedAgent();
            }
            
            var newAgent = await CreateNewAgent(configuration);
            AssignAgentToSession(newAgent.Id, sessionId);
            
            AgentCreated?.Invoke(this, new AgentPoolEventArgs 
            { 
                Agent = newAgent, 
                SessionId = sessionId 
            });
            
            return newAgent;
        }
        
        private async Task<Agent> CreateNewAgent(AgentConfiguration configuration)
        {
            configuration.Client = _client;
            
            if (configuration.EnableUserRules && _configuration.InheritUserRules)
            {
                configuration.SystemPrompt = await SystemPrompt.Create(
                    configuration.SystemPrompt,
                    includeDirectories: true,
                    includeUserRules: true);
            }
            
            var agent = new Agent(configuration);
            
            var entry = new AgentPoolEntry
            {
                Agent = agent,
                CreatedAt = DateTime.UtcNow,
                LastAccessTime = DateTime.UtcNow,
                Configuration = configuration,
                State = AgentState.Active
            };
            
            _agentPool[agent.Id] = entry;
            
            return agent;
        }
        
        private Agent? TryRecycleAgent(AgentConfiguration targetConfig)
        {
            if (!_configuration.EnableRecycling)
                return null;
            
            var recyclableAgent = _agentPool.Values
                .Where(e => e.State == AgentState.Idle &&
                           e.SessionId == null &&
                           IsConfigurationCompatible(e.Configuration, targetConfig))
                .OrderBy(e => e.LastAccessTime)
                .FirstOrDefault();
            
            if (recyclableAgent != null)
            {
                recyclableAgent.Agent.ClearHistory();
                recyclableAgent.LastAccessTime = DateTime.UtcNow;
                recyclableAgent.State = AgentState.Active;
                recyclableAgent.RecycleCount++;
                return recyclableAgent.Agent;
            }
            
            return null;
        }
        
        private bool IsConfigurationCompatible(AgentConfiguration config1, AgentConfiguration config2)
        {
            return config1.Model == config2.Model &&
                   config1.EnableTools == config2.EnableTools &&
                   Math.Abs((config1.Temperature ?? 0.7) - (config2.Temperature ?? 0.7)) < 0.1 &&
                   config1.EnableStreaming == config2.EnableStreaming;
        }
        
        private void AssignAgentToSession(string agentId, string sessionId)
        {
            _sessionAgentMapping[sessionId] = agentId;
            
            if (_agentPool.TryGetValue(agentId, out var entry))
            {
                entry.SessionId = sessionId;
                entry.State = AgentState.Active;
            }
        }
        
        public void ReleaseAgent(string sessionId)
        {
            if (_sessionAgentMapping.TryRemove(sessionId, out var agentId))
            {
                if (_agentPool.TryGetValue(agentId, out var entry))
                {
                    entry.SessionId = null;
                    entry.State = AgentState.Idle;
                    entry.LastAccessTime = DateTime.UtcNow;
                }
            }
        }
        
        public async Task DestroyAgentAsync(string agentId)
        {
            if (_agentPool.TryRemove(agentId, out var entry))
            {
                entry.State = AgentState.Destroyed;
                entry.Agent.Dispose();
                
                var sessionId = _sessionAgentMapping
                    .FirstOrDefault(kvp => kvp.Value == agentId).Key;
                    
                if (!string.IsNullOrEmpty(sessionId))
                {
                    _sessionAgentMapping.TryRemove(sessionId, out _);
                }
                
                AgentDestroyed?.Invoke(this, new AgentPoolEventArgs 
                { 
                    Agent = entry.Agent, 
                    SessionId = sessionId 
                });
                
                await Task.CompletedTask;
            }
        }
        
        private async Task EvictLeastRecentlyUsedAgent()
        {
            var lruAgent = _agentPool.Values
                .Where(e => e.State == AgentState.Idle)
                .OrderBy(e => e.LastAccessTime)
                .FirstOrDefault();
            
            if (lruAgent != null)
            {
                await DestroyAgentAsync(lruAgent.Agent.Id);
            }
        }
        
        private void CleanupIdleAgents(object? state)
        {
            var now = DateTime.UtcNow;
            var agentsToCleanup = _agentPool.Values
                .Where(e => e.State == AgentState.Idle &&
                           (now - e.LastAccessTime) > _configuration.MaxIdleTime)
                .Select(e => e.Agent.Id)
                .ToList();
            
            foreach (var agentId in agentsToCleanup)
            {
                _ = DestroyAgentAsync(agentId);
            }
        }
        
        public AgentPoolStatistics GetStatistics()
        {
            return new AgentPoolStatistics
            {
                TotalAgents = _agentPool.Count,
                ActiveAgents = _agentPool.Values.Count(e => e.State == AgentState.Active),
                IdleAgents = _agentPool.Values.Count(e => e.State == AgentState.Idle),
                TotalRecycles = _agentPool.Values.Sum(e => e.RecycleCount),
                AverageAccessCount = _agentPool.Values.Any() 
                    ? _agentPool.Values.Average(e => e.AccessCount) 
                    : 0,
                OldestAgentAge = _agentPool.Values.Any()
                    ? DateTime.UtcNow - _agentPool.Values.Min(e => e.CreatedAt)
                    : TimeSpan.Zero
            };
        }
        
        public void Dispose()
        {
            _cleanupTimer?.Dispose();
            
            foreach (var entry in _agentPool.Values)
            {
                entry.Agent.Dispose();
            }
            
            _agentPool.Clear();
            _sessionAgentMapping.Clear();
        }
        
        private class AgentPoolEntry
        {
            public Agent Agent { get; set; } = null!;
            public AgentConfiguration Configuration { get; set; } = null!;
            public DateTime CreatedAt { get; set; }
            public DateTime LastAccessTime { get; set; }
            public string? SessionId { get; set; }
            public AgentState State { get; set; }
            public int AccessCount { get; set; }
            public int RecycleCount { get; set; }
        }
        
        private enum AgentState
        {
            Active,
            Idle,
            Destroyed
        }
    }
    
    public class AgentPoolConfiguration
    {
        public int MaxPoolSize { get; set; } = 50;
        public TimeSpan MaxIdleTime { get; set; } = TimeSpan.FromMinutes(15);
        public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromMinutes(5);
        public bool EnableRecycling { get; set; } = true;
        public bool InheritUserRules { get; set; } = true;
    }
    
    public class AgentPoolStatistics
    {
        public int TotalAgents { get; set; }
        public int ActiveAgents { get; set; }
        public int IdleAgents { get; set; }
        public int TotalRecycles { get; set; }
        public double AverageAccessCount { get; set; }
        public TimeSpan OldestAgentAge { get; set; }
    }
    
    public class AgentPoolEventArgs : EventArgs
    {
        public Agent Agent { get; set; } = null!;
        public string? SessionId { get; set; }
    }
}