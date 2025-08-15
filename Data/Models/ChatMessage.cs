using System;
using System.Text.Json;

namespace Saturn.Data.Models;

public class ChatMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string SessionId { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? AgentName { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public int SequenceNumber { get; set; }
    public string? ToolCallsJson { get; set; }
    public string? ToolCallId { get; set; }
}