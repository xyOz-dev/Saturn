using System;
using System.Threading.Tasks;

namespace Saturn.Core.Abstractions.ProgressReporters
{
    public class ConsoleProgressReporter : IProgressReporter
    {
        private readonly bool _verbose;
        private readonly object _lock = new();
        
        public ConsoleProgressReporter(bool verbose = false)
        {
            _verbose = verbose;
        }
        
        public Task ReportStatusAsync(StatusUpdate status)
        {
            lock (_lock)
            {
                var color = status.Level switch
                {
                    StatusLevel.Debug => ConsoleColor.Gray,
                    StatusLevel.Info => ConsoleColor.White,
                    StatusLevel.Warning => ConsoleColor.Yellow,
                    StatusLevel.Error => ConsoleColor.Red,
                    StatusLevel.Critical => ConsoleColor.DarkRed,
                    _ => ConsoleColor.White
                };
                
                if (status.Level == StatusLevel.Debug && !_verbose)
                    return Task.CompletedTask;
                
                Console.ForegroundColor = color;
                Console.WriteLine($"[{status.Timestamp:HH:mm:ss}] {status.Status}: {status.Message ?? ""}");
                Console.ResetColor();
            }
            
            return Task.CompletedTask;
        }
        
        public Task ReportProgressAsync(ProgressUpdate progress)
        {
            lock (_lock)
            {
                var progressBar = GenerateProgressBar(progress.Percentage);
                Console.Write($"\r{progressBar} {progress.Percentage:F1}% - {progress.CurrentStep ?? "Processing..."}");
                
                if (progress.Percentage >= 100)
                {
                    Console.WriteLine();
                }
            }
            
            return Task.CompletedTask;
        }
        
        public Task ReportToolExecutionAsync(ToolExecutionUpdate toolExecution)
        {
            if (!_verbose && toolExecution.Status == ToolExecutionStatus.Running)
                return Task.CompletedTask;
            
            lock (_lock)
            {
                var statusSymbol = toolExecution.Status switch
                {
                    ToolExecutionStatus.Started => "▶",
                    ToolExecutionStatus.Running => "⚙",
                    ToolExecutionStatus.Completed => "✓",
                    ToolExecutionStatus.Failed => "✗",
                    ToolExecutionStatus.Cancelled => "⊘",
                    _ => "?"
                };
                
                var color = toolExecution.Status switch
                {
                    ToolExecutionStatus.Completed => ConsoleColor.Green,
                    ToolExecutionStatus.Failed => ConsoleColor.Red,
                    ToolExecutionStatus.Cancelled => ConsoleColor.Yellow,
                    _ => ConsoleColor.Cyan
                };
                
                Console.ForegroundColor = color;
                Console.Write($"{statusSymbol} {toolExecution.ToolName}");
                
                if (toolExecution.Duration.HasValue)
                {
                    Console.Write($" ({toolExecution.Duration.Value.TotalMilliseconds:F0}ms)");
                }
                
                if (!string.IsNullOrEmpty(toolExecution.Error))
                {
                    Console.Write($" - Error: {toolExecution.Error}");
                }
                
                Console.WriteLine();
                Console.ResetColor();
            }
            
            return Task.CompletedTask;
        }
        
        public Task ReportErrorAsync(ErrorUpdate error)
        {
            lock (_lock)
            {
                var color = error.Severity switch
                {
                    ErrorSeverity.Warning => ConsoleColor.Yellow,
                    ErrorSeverity.Error => ConsoleColor.Red,
                    ErrorSeverity.Fatal => ConsoleColor.DarkRed,
                    _ => ConsoleColor.Red
                };
                
                Console.ForegroundColor = color;
                Console.WriteLine($"[{error.Severity}] {error.Error}");
                
                if (!string.IsNullOrEmpty(error.Context))
                {
                    Console.WriteLine($"  Context: {error.Context}");
                }
                
                if (_verbose && !string.IsNullOrEmpty(error.StackTrace))
                {
                    Console.WriteLine($"  Stack: {error.StackTrace}");
                }
                
                Console.ResetColor();
            }
            
            return Task.CompletedTask;
        }
        
        public Task ReportCompletionAsync(CompletionUpdate completion)
        {
            lock (_lock)
            {
                Console.ForegroundColor = completion.Success ? ConsoleColor.Green : ConsoleColor.Red;
                Console.WriteLine($"\n{(completion.Success ? "✓" : "✗")} Task completed in {completion.Duration.TotalSeconds:F2}s");
                
                if (!string.IsNullOrEmpty(completion.Summary))
                {
                    Console.WriteLine($"  {completion.Summary}");
                }
                
                Console.ResetColor();
            }
            
            return Task.CompletedTask;
        }
        
        private string GenerateProgressBar(double percentage, int width = 30)
        {
            var filled = (int)(width * percentage / 100);
            var empty = width - filled;
            return $"[{new string('█', filled)}{new string('░', empty)}]";
        }
    }
}