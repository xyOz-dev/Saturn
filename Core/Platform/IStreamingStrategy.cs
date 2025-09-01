using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Saturn.Core.Sessions;
using Saturn.OpenRouter.Models.Api.Chat;

namespace Saturn.Core.Platform
{
    public interface IStreamingStrategy
    {
        string StrategyName { get; }
        StreamingMode Mode { get; }
        
        Task InitializeStreamAsync(string streamId, StreamingContext context);
        Task SendChunkAsync(string streamId, StreamChunk chunk);
        Task FlushBufferAsync(string streamId);
        Task CompleteStreamAsync(string streamId, string? finalContent = null);
        Task CancelStreamAsync(string streamId, string? reason = null);
        bool IsStreamActive(string streamId);
        
        event EventHandler<StreamingEventArgs>? StreamStarted;
        event EventHandler<StreamingEventArgs>? StreamCompleted;
        event EventHandler<StreamingErrorEventArgs>? StreamError;
    }
    
    public enum StreamingMode
    {
        Disabled,
        ServerSentEvents,
        WebSocket,
        LongPolling,
        Chunked,
        Buffered
    }
    
    public class StreamingContext
    {
        public string? UserId { get; set; }
        public string? ChannelId { get; set; }
        public string? SessionId { get; set; }
        public PlatformType Platform { get; set; }
        public int BufferSize { get; set; } = 1024;
        public TimeSpan FlushInterval { get; set; } = TimeSpan.FromMilliseconds(100);
        public bool AutoFlush { get; set; } = true;
        public bool SendPartialTokens { get; set; } = true;
        public Dictionary<string, object> Metadata { get; set; } = new();
    }
    
    public class StreamingEventArgs : EventArgs
    {
        public string StreamId { get; set; } = string.Empty;
        public StreamingContext Context { get; set; } = null!;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
    
    public class StreamingErrorEventArgs : StreamingEventArgs
    {
        public Exception Exception { get; set; } = null!;
        public bool IsRecoverable { get; set; }
    }
}