using System;
using System.Collections.Concurrent;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Saturn.OpenRouter.Models.Api.Chat;

namespace Saturn.Core.Platform.Streaming
{
    public class BufferedStreamingStrategy : IStreamingStrategy, IDisposable
    {
        private readonly ConcurrentDictionary<string, StreamBuffer> _streams = new();
        private readonly Timer _flushTimer;
        
        public string StrategyName => "Buffered";
        public StreamingMode Mode => StreamingMode.Buffered;
        
        public event EventHandler<StreamingEventArgs>? StreamStarted;
        public event EventHandler<StreamingEventArgs>? StreamCompleted;
        public event EventHandler<StreamingErrorEventArgs>? StreamError;
        
        public BufferedStreamingStrategy()
        {
            _flushTimer = new Timer(AutoFlushStreams, null, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100));
        }
        
        public Task InitializeStreamAsync(string streamId, StreamingContext context)
        {
            var buffer = new StreamBuffer
            {
                StreamId = streamId,
                Context = context,
                StartTime = DateTime.UtcNow,
                Buffer = new StringBuilder(),
                LastFlushTime = DateTime.UtcNow
            };
            
            _streams[streamId] = buffer;
            
            StreamStarted?.Invoke(this, new StreamingEventArgs 
            { 
                StreamId = streamId, 
                Context = context 
            });
            
            return Task.CompletedTask;
        }
        
        public Task SendChunkAsync(string streamId, StreamChunk chunk)
        {
            if (!_streams.TryGetValue(streamId, out var buffer))
            {
                throw new InvalidOperationException($"Stream {streamId} not initialized");
            }
            
            lock (buffer.LockObject)
            {
                if (chunk.IsToolCall)
                {
                    buffer.PendingToolCalls.Add(chunk);
                }
                else if (!string.IsNullOrEmpty(chunk.Content))
                {
                    buffer.Buffer.Append(chunk.Content);
                    buffer.TokenCount++;
                }
                
                if (chunk.IsComplete)
                {
                    buffer.IsComplete = true;
                }
                
                if (ShouldFlush(buffer))
                {
                    _ = FlushBufferAsync(streamId);
                }
            }
            
            return Task.CompletedTask;
        }
        
        public async Task FlushBufferAsync(string streamId)
        {
            if (!_streams.TryGetValue(streamId, out var buffer))
                return;
            
            string? contentToSend = null;
            List<StreamChunk>? toolCallsToSend = null;
            
            lock (buffer.LockObject)
            {
                if (buffer.Buffer.Length > 0)
                {
                    contentToSend = buffer.Buffer.ToString();
                    buffer.Buffer.Clear();
                }
                
                if (buffer.PendingToolCalls.Count > 0)
                {
                    toolCallsToSend = new List<StreamChunk>(buffer.PendingToolCalls);
                    buffer.PendingToolCalls.Clear();
                }
                
                buffer.LastFlushTime = DateTime.UtcNow;
            }
            
            if (!string.IsNullOrEmpty(contentToSend) || toolCallsToSend?.Count > 0)
            {
                if (buffer.OnFlush != null)
                {
                    await buffer.OnFlush(contentToSend, toolCallsToSend);
                }
            }
        }
        
        public async Task CompleteStreamAsync(string streamId, string? finalContent = null)
        {
            if (!_streams.TryGetValue(streamId, out var buffer))
                return;
            
            await FlushBufferAsync(streamId);
            
            if (!string.IsNullOrEmpty(finalContent) && buffer.OnFlush != null)
            {
                await buffer.OnFlush(finalContent, null);
            }
            
            _streams.TryRemove(streamId, out _);
            
            StreamCompleted?.Invoke(this, new StreamingEventArgs 
            { 
                StreamId = streamId, 
                Context = buffer.Context 
            });
        }
        
        public Task CancelStreamAsync(string streamId, string? reason = null)
        {
            if (_streams.TryRemove(streamId, out var buffer))
            {
                buffer.IsCancelled = true;
                
                StreamError?.Invoke(this, new StreamingErrorEventArgs
                {
                    StreamId = streamId,
                    Context = buffer.Context,
                    Exception = new OperationCanceledException(reason ?? "Stream cancelled"),
                    IsRecoverable = false
                });
            }
            
            return Task.CompletedTask;
        }
        
        public bool IsStreamActive(string streamId)
        {
            return _streams.ContainsKey(streamId);
        }
        
        private bool ShouldFlush(StreamBuffer buffer)
        {
            if (!buffer.Context.AutoFlush)
                return false;
            
            if (buffer.IsComplete)
                return true;
            
            if (buffer.Buffer.Length >= buffer.Context.BufferSize)
                return true;
            
            if (DateTime.UtcNow - buffer.LastFlushTime > buffer.Context.FlushInterval)
                return true;
            
            return false;
        }
        
        private void AutoFlushStreams(object? state)
        {
            var now = DateTime.UtcNow;
            
            foreach (var kvp in _streams)
            {
                var buffer = kvp.Value;
                
                if (buffer.Context.AutoFlush && 
                    (now - buffer.LastFlushTime) > buffer.Context.FlushInterval &&
                    buffer.Buffer.Length > 0)
                {
                    _ = FlushBufferAsync(kvp.Key);
                }
            }
        }
        
        public void SetFlushCallback(string streamId, Func<string?, List<StreamChunk>?, Task> callback)
        {
            if (_streams.TryGetValue(streamId, out var buffer))
            {
                buffer.OnFlush = callback;
            }
        }
        
        public StreamStatistics GetStatistics(string streamId)
        {
            if (!_streams.TryGetValue(streamId, out var buffer))
            {
                return new StreamStatistics();
            }
            
            return new StreamStatistics
            {
                StreamId = streamId,
                StartTime = buffer.StartTime,
                Duration = DateTime.UtcNow - buffer.StartTime,
                TokenCount = buffer.TokenCount,
                BufferSize = buffer.Buffer.Length,
                IsComplete = buffer.IsComplete,
                IsCancelled = buffer.IsCancelled
            };
        }
        
        public void Dispose()
        {
            _flushTimer?.Dispose();
            
            foreach (var streamId in _streams.Keys.ToList())
            {
                _ = CancelStreamAsync(streamId, "Strategy disposed");
            }
            
            _streams.Clear();
        }
        
        private class StreamBuffer
        {
            public string StreamId { get; set; } = string.Empty;
            public StreamingContext Context { get; set; } = null!;
            public StringBuilder Buffer { get; set; } = new();
            public List<StreamChunk> PendingToolCalls { get; set; } = new();
            public DateTime StartTime { get; set; }
            public DateTime LastFlushTime { get; set; }
            public int TokenCount { get; set; }
            public bool IsComplete { get; set; }
            public bool IsCancelled { get; set; }
            public object LockObject { get; } = new();
            public Func<string?, List<StreamChunk>?, Task>? OnFlush { get; set; }
        }
    }
    
    public class StreamStatistics
    {
        public string StreamId { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
        public TimeSpan Duration { get; set; }
        public int TokenCount { get; set; }
        public int BufferSize { get; set; }
        public bool IsComplete { get; set; }
        public bool IsCancelled { get; set; }
    }
}