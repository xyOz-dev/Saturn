using System;
using System.Collections.Generic;

namespace Saturn.Core.Sessions
{
    public class PlatformSession
    {
        public string SessionId { get; init; } = Guid.NewGuid().ToString();
        public string? UserId { get; set; }
        public string? ChannelId { get; set; }
        public PlatformType Platform { get; set; }
        public string? PlatformSpecificId { get; set; }
        public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
        public DateTime LastActivityAt { get; private set; } = DateTime.UtcNow;
        public SessionState State { get; set; } = SessionState.Active;
        public Dictionary<string, object> Metadata { get; set; } = new();
        public SessionConfiguration Configuration { get; set; } = new();
        
        public string? AgentId { get; set; }
        public string? WorkspaceId { get; set; }
        public string? ParentSessionId { get; set; }
        public List<string> ChildSessionIds { get; set; } = new();
        
        public void UpdateActivity()
        {
            LastActivityAt = DateTime.UtcNow;
        }
        
        internal void Touch()
        {
            LastActivityAt = DateTime.UtcNow;
        }
        
        public TimeSpan GetIdleTime()
        {
            return DateTime.UtcNow - LastActivityAt;
        }
        
        public bool IsExpired(TimeSpan maxIdleTime)
        {
            return GetIdleTime() > maxIdleTime;
        }
    }
    
    public enum PlatformType
    {
        Console,
        Web,
        Discord,
        Slack,
        Teams,
        API,
        Test
    }
    
    public enum SessionState
    {
        Active,
        Idle,
        Suspended,
        Terminated,
        Error
    }
    
    public class SessionConfiguration
    {
        public string? PreferredModel { get; set; }
        public double? Temperature { get; set; }
        public int? MaxTokens { get; set; }
        public bool EnableStreaming { get; set; } = true;
        public bool RequireApproval { get; set; } = true;
        public bool EnableTools { get; set; } = true;
        public List<string> AllowedTools { get; set; } = new();
        public List<string> DisallowedTools { get; set; } = new();
        public TimeSpan SessionTimeout { get; set; } = TimeSpan.FromMinutes(30);
        public int MaxMessageLength { get; set; } = 4000;
        public bool EnableUserRules { get; set; } = true;
        public Dictionary<string, object> PlatformSpecificSettings { get; set; } = new();
    }
}