using System;
using System.Threading.Tasks;

namespace Saturn.Core.Abstractions
{
    public interface IProgressReporter
    {
        Task ReportStatusAsync(StatusUpdate status);
        Task ReportProgressAsync(ProgressUpdate progress);
        Task ReportToolExecutionAsync(ToolExecutionUpdate toolExecution);
        Task ReportErrorAsync(ErrorUpdate error);
        Task ReportCompletionAsync(CompletionUpdate completion);
    }
    
    public abstract class UpdateBase
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string? SessionId { get; set; }
        public string? AgentId { get; set; }
        public string? UserId { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public Dictionary<string, object> Metadata { get; set; } = new();
    }
    
    public class StatusUpdate : UpdateBase
    {
        public string Status { get; set; } = string.Empty;
        public string? PreviousStatus { get; set; }
        public string? Message { get; set; }
        public StatusLevel Level { get; set; } = StatusLevel.Info;
    }
    
    public enum StatusLevel
    {
        Debug,
        Info,
        Warning,
        Error,
        Critical
    }
    
    public class ProgressUpdate : UpdateBase
    {
        public double Percentage { get; set; }
        public string? CurrentStep { get; set; }
        public int? TotalSteps { get; set; }
        public int? CurrentStepNumber { get; set; }
        public string? Message { get; set; }
        public TimeSpan? EstimatedTimeRemaining { get; set; }
    }
    
    public class ToolExecutionUpdate : UpdateBase
    {
        public string ToolName { get; set; } = string.Empty;
        public string? ToolId { get; set; }
        public ToolExecutionStatus Status { get; set; }
        public Dictionary<string, object>? Parameters { get; set; }
        public object? Result { get; set; }
        public string? Error { get; set; }
        public TimeSpan? Duration { get; set; }
    }
    
    public enum ToolExecutionStatus
    {
        Started,
        Running,
        Completed,
        Failed,
        Cancelled
    }
    
    public class ErrorUpdate : UpdateBase
    {
        public string Error { get; set; } = string.Empty;
        public string? StackTrace { get; set; }
        public ErrorSeverity Severity { get; set; } = ErrorSeverity.Error;
        public string? Context { get; set; }
        public bool IsRecoverable { get; set; } = true;
    }
    
    public enum ErrorSeverity
    {
        Warning,
        Error,
        Fatal
    }
    
    public class CompletionUpdate : UpdateBase
    {
        public bool Success { get; set; }
        public string? Result { get; set; }
        public TimeSpan Duration { get; set; }
        public Dictionary<string, object>? Statistics { get; set; }
        public string? Summary { get; set; }
    }
}