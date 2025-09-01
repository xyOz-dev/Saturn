using System;
using System.Threading.Tasks;
using Saturn.Core.Abstractions;
using Saturn.Core.Sessions;
using Saturn.OpenRouter.Models.Api.Chat;

namespace Saturn.Core.Platform
{
    public interface IPlatformAdapter
    {
        string PlatformName { get; }
        PlatformType PlatformType { get; }
        PlatformCapabilities Capabilities { get; }
        
        Task InitializeAsync(PlatformConfiguration configuration);
        Task<PlatformMessage> ConvertToPlatformMessageAsync(Message message);
        Task<Message> ConvertFromPlatformMessageAsync(object platformMessage);
        Task SendMessageAsync(string channelId, PlatformMessage message);
        Task SendStreamingChunkAsync(string channelId, StreamChunk chunk);
        Task<bool> ValidateMessageAsync(object platformMessage);
        
        IApprovalStrategy GetApprovalStrategy();
        IProgressReporter GetProgressReporter();
        IResponseFormatter GetResponseFormatter();
        IStreamingStrategy GetStreamingStrategy();
        
        event EventHandler<PlatformMessageEventArgs>? MessageReceived;
        event EventHandler<PlatformConnectionEventArgs>? Connected;
        event EventHandler<PlatformConnectionEventArgs>? Disconnected;
        event EventHandler<PlatformErrorEventArgs>? ErrorOccurred;
    }
    
    public class PlatformCapabilities
    {
        public bool SupportsStreaming { get; set; }
        public bool SupportsThreading { get; set; }
        public bool SupportsReactions { get; set; }
        public bool SupportsEmbeds { get; set; }
        public bool SupportsAttachments { get; set; }
        public bool SupportsButtons { get; set; }
        public bool SupportsEphemeralMessages { get; set; }
        public int MaxMessageLength { get; set; } = 2000;
        public int MaxEmbedCount { get; set; } = 0;
        public int MaxAttachmentSize { get; set; } = 0;
        public string[] SupportedFileTypes { get; set; } = Array.Empty<string>();
        public TimeSpan? MessageEditWindow { get; set; }
        public RateLimits? RateLimits { get; set; }
    }
    
    public class RateLimits
    {
        public int MessagesPerMinute { get; set; }
        public int MessagesPerHour { get; set; }
        public int CharactersPerMessage { get; set; }
        public TimeSpan CooldownPeriod { get; set; }
    }
    
    public class PlatformConfiguration
    {
        public string? ApiKey { get; set; }
        public string? ApiSecret { get; set; }
        public string? WebhookUrl { get; set; }
        public Dictionary<string, string> CustomHeaders { get; set; } = new();
        public Dictionary<string, object> PlatformSpecificConfig { get; set; } = new();
        public bool EnableLogging { get; set; } = true;
        public bool AutoReconnect { get; set; } = true;
        public TimeSpan ReconnectDelay { get; set; } = TimeSpan.FromSeconds(5);
    }
    
    public class PlatformMessage
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string ChannelId { get; set; } = string.Empty;
        public string? UserId { get; set; }
        public string? Username { get; set; }
        public string Content { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public PlatformMessageType Type { get; set; } = PlatformMessageType.Text;
        public List<PlatformEmbed>? Embeds { get; set; }
        public List<PlatformAttachment>? Attachments { get; set; }
        public List<PlatformButton>? Buttons { get; set; }
        public string? ReplyToId { get; set; }
        public bool IsEphemeral { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new();
    }
    
    public enum PlatformMessageType
    {
        Text,
        Embed,
        File,
        Image,
        Audio,
        Video,
        Code,
        System
    }
    
    public class PlatformEmbed
    {
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? Url { get; set; }
        public int? Color { get; set; }
        public List<PlatformEmbedField>? Fields { get; set; }
        public string? Footer { get; set; }
        public string? ThumbnailUrl { get; set; }
        public string? ImageUrl { get; set; }
    }
    
    public class PlatformEmbedField
    {
        public string Name { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public bool Inline { get; set; }
    }
    
    public class PlatformAttachment
    {
        public string Id { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string? ContentType { get; set; }
        public long Size { get; set; }
        public string? Url { get; set; }
        public byte[]? Data { get; set; }
    }
    
    public class PlatformButton
    {
        public string Id { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public PlatformButtonStyle Style { get; set; } = PlatformButtonStyle.Primary;
        public string? Emoji { get; set; }
        public string? Url { get; set; }
        public bool Disabled { get; set; }
    }
    
    public enum PlatformButtonStyle
    {
        Primary,
        Secondary,
        Success,
        Danger,
        Link
    }
    
    public class PlatformMessageEventArgs : EventArgs
    {
        public required PlatformMessage Message { get; set; }
        public PlatformSession? Session { get; set; }
    }
    
    public class PlatformConnectionEventArgs : EventArgs
    {
        public bool IsReconnect { get; set; }
        public string? Reason { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
    
    public class PlatformErrorEventArgs : EventArgs
    {
        public required Exception Exception { get; set; }
        public string Context { get; set; } = string.Empty;
        public bool IsRecoverable { get; set; }
    }
}