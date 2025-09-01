using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Saturn.Core.Security
{
    public interface IToolSecurityContext
    {
        Task<ToolPermission> CheckPermissionAsync(ToolAccessRequest request);
        Task<bool> IsToolAllowedAsync(string toolName, SecurityPrincipal principal);
        Task<bool> ValidateParametersAsync(string toolName, Dictionary<string, object> parameters, SecurityPrincipal principal);
        Task LogToolAccessAsync(ToolAccessLog log);
        ToolSecurityPolicy GetPolicy(string toolName);
        void SetPolicy(string toolName, ToolSecurityPolicy policy);
    }
    
    public class ToolAccessRequest
    {
        public string ToolName { get; set; } = string.Empty;
        public Dictionary<string, object> Parameters { get; set; } = new();
        public required SecurityPrincipal Principal { get; set; }
        public string? SessionId { get; set; }
        public string? Context { get; set; }
        public DateTime RequestTime { get; set; } = DateTime.UtcNow;
    }
    
    public class SecurityPrincipal
    {
        public string Id { get; set; } = string.Empty;
        public string? UserId { get; set; }
        public string? ChannelId { get; set; }
        public PrincipalType Type { get; set; }
        public List<string> Roles { get; set; } = new();
        public Dictionary<string, object> Claims { get; set; } = new();
        public bool IsAuthenticated { get; set; }
        public DateTime? AuthenticatedAt { get; set; }
    }
    
    public enum PrincipalType
    {
        User,
        System,
        Service,
        Anonymous
    }
    
    public class ToolPermission
    {
        public bool IsAllowed { get; set; }
        public string? Reason { get; set; }
        public List<ParameterRestriction>? ParameterRestrictions { get; set; }
        public RateLimitInfo? RateLimit { get; set; }
        public List<string>? RequiredApprovals { get; set; }
        public SecurityLevel Level { get; set; } = SecurityLevel.Normal;
    }
    
    public class ParameterRestriction
    {
        public string ParameterName { get; set; } = string.Empty;
        public RestrictionType Type { get; set; }
        public object? AllowedValues { get; set; }
        public object? ForbiddenValues { get; set; }
        public string? Pattern { get; set; }
        public string? ValidationMessage { get; set; }
    }
    
    public enum RestrictionType
    {
        None,
        Whitelist,
        Blacklist,
        Pattern,
        Range,
        Custom
    }
    
    public class RateLimitInfo
    {
        public int MaxCalls { get; set; }
        public TimeSpan Period { get; set; }
        public int CurrentCount { get; set; }
        public DateTime ResetTime { get; set; }
        public bool IsExceeded => CurrentCount >= MaxCalls;
        
        // Multi-window support
        public int MinuteCount { get; set; }
        public int HourCount { get; set; }
        public int DayCount { get; set; }
        public int MaxPerMinute { get; set; }
        public int MaxPerHour { get; set; }
        public int MaxPerDay { get; set; }
        public DateTime MinuteResetTime { get; set; }
        public DateTime HourResetTime { get; set; }
        public DateTime DayResetTime { get; set; }
        public bool IsMinuteExceeded => MaxPerMinute > 0 && MinuteCount >= MaxPerMinute;
        public bool IsHourExceeded => MaxPerHour > 0 && HourCount >= MaxPerHour;
        public bool IsDayExceeded => MaxPerDay > 0 && DayCount >= MaxPerDay;
        public bool IsAnyLimitExceeded => IsMinuteExceeded || IsHourExceeded || IsDayExceeded;
    }
    
    public enum SecurityLevel
    {
        Public,
        Normal,
        Elevated,
        Critical,
        Restricted
    }
    
    public class ToolSecurityPolicy
    {
        public string ToolName { get; set; } = string.Empty;
        public SecurityLevel RequiredLevel { get; set; } = SecurityLevel.Normal;
        public List<string> AllowedRoles { get; set; } = new();
        public List<string> DeniedRoles { get; set; } = new();
        public List<string> AllowedUsers { get; set; } = new();
        public List<string> DeniedUsers { get; set; } = new();
        public List<string> AllowedChannels { get; set; } = new();
        public List<string> DeniedChannels { get; set; } = new();
        public bool RequireAuthentication { get; set; } = true;
        public bool RequireApproval { get; set; } = false;
        public List<ParameterRestriction> ParameterRestrictions { get; set; } = new();
        public RateLimitPolicy? RateLimit { get; set; }
        public AuditPolicy? Audit { get; set; }
    }
    
    public class RateLimitPolicy
    {
        public int MaxCallsPerMinute { get; set; }
        public int MaxCallsPerHour { get; set; }
        public int MaxCallsPerDay { get; set; }
        public bool PerUser { get; set; } = true;
        public bool PerChannel { get; set; } = false;
        public Dictionary<string, int> CustomLimits { get; set; } = new();
    }
    
    public class AuditPolicy
    {
        public bool LogAllAccess { get; set; } = true;
        public bool LogParameters { get; set; } = true;
        public bool LogResults { get; set; } = false;
        public bool LogFailures { get; set; } = true;
        public List<string> SensitiveParameters { get; set; } = new();
    }
    
    public class ToolAccessLog
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string ToolName { get; set; } = string.Empty;
        public required SecurityPrincipal Principal { get; set; }
        public Dictionary<string, object>? Parameters { get; set; }
        public bool WasAllowed { get; set; }
        public string? DenialReason { get; set; }
        public DateTime AccessTime { get; set; } = DateTime.UtcNow;
        public TimeSpan? ExecutionTime { get; set; }
        public bool Success { get; set; }
        public string? Error { get; set; }
        public string? SessionId { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new();
    }
}