using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Saturn.Core.Sessions;
using Saturn.OpenRouter.Models.Api.Chat;

namespace Saturn.Core.Platform
{
    public interface IResponseFormatter
    {
        string FormatName { get; }
        
        Task<FormattedResponse> FormatMessageAsync(Message message, FormattingContext context);
        Task<FormattedResponse> FormatErrorAsync(string error, ErrorLevel level, FormattingContext context);
        Task<FormattedResponse> FormatToolResultAsync(string toolName, object result, FormattingContext context);
        Task<FormattedResponse> FormatCodeBlockAsync(string code, string? language, FormattingContext context);
        Task<List<FormattedResponse>> SplitLongMessageAsync(string content, FormattingContext context);
        string TruncateMessage(string content, int maxLength, string suffix = "...");
        string SanitizeContent(string content);
    }
    
    public class FormattingContext
    {
        public PlatformType Platform { get; set; }
        public int MaxLength { get; set; } = 2000;
        public bool SupportsMarkdown { get; set; } = true;
        public bool SupportsEmbeds { get; set; } = false;
        public bool SupportsCodeBlocks { get; set; } = true;
        public bool SupportsInlineCode { get; set; } = true;
        public bool SupportsColors { get; set; } = false;
        public bool PreferPlainText { get; set; } = false;
        public string? UserId { get; set; }
        public string? ChannelId { get; set; }
        public Dictionary<string, object> CustomOptions { get; set; } = new();
    }
    
    public class FormattedResponse
    {
        public string Content { get; set; } = string.Empty;
        public ResponseFormat Format { get; set; } = ResponseFormat.Text;
        public PlatformEmbed? Embed { get; set; }
        public List<PlatformButton>? Buttons { get; set; }
        public List<PlatformAttachment>? Attachments { get; set; }
        public bool RequiresSplit { get; set; }
        public int? Color { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new();
    }
    
    public enum ResponseFormat
    {
        Text,
        Markdown,
        Html,
        Embed,
        Code,
        PlainText
    }
    
    public enum ErrorLevel
    {
        Info,
        Warning,
        Error,
        Critical
    }
}