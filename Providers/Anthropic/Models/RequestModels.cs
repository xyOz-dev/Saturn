using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Saturn.Providers.Anthropic.Models
{
    public class AnthropicChatRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;
        
        [JsonPropertyName("messages")]
        public List<AnthropicMessage> Messages { get; set; } = new();
        
        [JsonPropertyName("max_tokens")]
        public int MaxTokens { get; set; } = 4096;
        
        [JsonPropertyName("temperature")]
        public double? Temperature { get; set; }
        
        [JsonPropertyName("top_p")]
        public double? TopP { get; set; }
        
        [JsonPropertyName("stream")]
        public bool Stream { get; set; }
        
        [JsonPropertyName("tools")]
        public List<AnthropicTool>? Tools { get; set; }
        
        [JsonPropertyName("system")]
        public string? System { get; set; }
    }
    
    public class AnthropicMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = string.Empty; // "user" or "assistant"
        
        [JsonPropertyName("content")]
        public object Content { get; set; } = new object(); // Can be string or List<ContentBlock>
    }
    
    public class ContentBlock
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty; // "text" or "tool_use" or "tool_result"
        
        [JsonPropertyName("text")]
        public string? Text { get; set; }
        
        [JsonPropertyName("id")]
        public string? Id { get; set; }
        
        [JsonPropertyName("name")]
        public string? Name { get; set; }
        
        [JsonPropertyName("input")]
        public object? Input { get; set; } // Tool arguments as JSON object
        
        [JsonPropertyName("tool_use_id")]
        public string? ToolUseId { get; set; }
        
        [JsonPropertyName("content")]
        public string? ToolResultContent { get; set; }
    }
    
    public class AnthropicTool
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
        
        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;
        
        [JsonPropertyName("input_schema")]
        public object InputSchema { get; set; } = new object(); // JSON Schema object
    }
}