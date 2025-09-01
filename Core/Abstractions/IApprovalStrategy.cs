using System;
using System.Threading.Tasks;

namespace Saturn.Core.Abstractions
{
    public interface IApprovalStrategy
    {
        string PlatformName { get; }
        
        Task<ApprovalResult> RequestApprovalAsync(ApprovalRequest request);
        
        Task<bool> IsApprovalRequiredAsync(string command, string context);
        
        void Configure(ApprovalConfiguration configuration);
    }
    
    public class ApprovalRequest
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Command { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Context { get; set; } = string.Empty;
        public string? UserId { get; set; }
        public string? ChannelId { get; set; }
        public ApprovalLevel Level { get; set; } = ApprovalLevel.Normal;
        public int TimeoutSeconds { get; set; } = 30;
        public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
    }
    
    public class ApprovalResult
    {
        public bool Approved { get; set; }
        public string? Reason { get; set; }
        public string? ApprovedBy { get; set; }
        public DateTime RespondedAt { get; set; } = DateTime.UtcNow;
        public TimeSpan ResponseTime => RequestedAt == default(DateTime) ? TimeSpan.Zero : RespondedAt - RequestedAt;
        public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
    }
    
    public enum ApprovalLevel
    {
        Low,
        Normal,
        High,
        Critical
    }
    
    public class ApprovalConfiguration
    {
        public bool RequireApprovalByDefault { get; set; } = true;
        public int DefaultTimeoutSeconds { get; set; } = 30;
        public string[] AutoApprovedCommands { get; set; } = Array.Empty<string>();
        public string[] AlwaysRequireApprovalCommands { get; set; } = Array.Empty<string>();
        public Dictionary<string, ApprovalLevel> CommandLevels { get; set; } = new();
    }
}