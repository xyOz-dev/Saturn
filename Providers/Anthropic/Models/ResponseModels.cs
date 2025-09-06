using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Saturn.Providers.Anthropic.Models
{
    public class AnthropicChatResponse
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;
        
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;
        
        [JsonPropertyName("role")]
        public string Role { get; set; } = string.Empty;
        
        [JsonPropertyName("content")]
        public List<ContentBlock> Content { get; set; } = new();
        
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;
        
        [JsonPropertyName("stop_reason")]
        public string? StopReason { get; set; }
        
        [JsonPropertyName("stop_sequence")]
        public string? StopSequence { get; set; }
        
        [JsonPropertyName("usage")]
        public AnthropicUsage Usage { get; set; } = new();
    }
    
    public class AnthropicUsage
    {
        [JsonPropertyName("input_tokens")]
        public int InputTokens { get; set; }
        
        [JsonPropertyName("output_tokens")]
        public int OutputTokens { get; set; }
    }
    
    // Streaming response models
    public class AnthropicStreamEvent
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;
        
        [JsonPropertyName("message")]
        public AnthropicChatResponse? Message { get; set; }
        
        [JsonPropertyName("index")]
        public int? Index { get; set; }
        
        [JsonPropertyName("content_block")]
        public ContentBlock? ContentBlock { get; set; }
        
        [JsonPropertyName("delta")]
        public StreamDelta? Delta { get; set; }
        
        [JsonPropertyName("usage")]
        public AnthropicUsage? Usage { get; set; }
    }
    
    public class StreamDelta
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;
        
        [JsonPropertyName("text")]
        public string? Text { get; set; }
        
        [JsonPropertyName("partial_json")]
        public string? PartialJson { get; set; }
        
        [JsonPropertyName("stop_reason")]
        public string? StopReason { get; set; }
    }
}