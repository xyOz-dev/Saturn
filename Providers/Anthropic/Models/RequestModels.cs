using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Saturn.Providers.Anthropic.Models
{
    public class AnthropicChatRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; }
        
        [JsonPropertyName("messages")]
        public List<AnthropicMessage> Messages { get; set; }
        
        [JsonPropertyName("max_tokens")]
        public int MaxTokens { get; set; } = 4096;
        
        [JsonPropertyName("temperature")]
        public double? Temperature { get; set; }
        
        [JsonPropertyName("top_p")]
        public double? TopP { get; set; }
        
        [JsonPropertyName("stream")]
        public bool Stream { get; set; }
        
        [JsonPropertyName("tools")]
        public List<AnthropicTool> Tools { get; set; }
        
        [JsonPropertyName("system")]
        public string System { get; set; }
    }
    
    public class AnthropicMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } // "user" or "assistant"
        
        [JsonPropertyName("content")]
        public object Content { get; set; } // Can be string or List<ContentBlock>
    }
    
    public class ContentBlock
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } // "text" or "tool_use" or "tool_result"
        
        [JsonPropertyName("text")]
        public string Text { get; set; }
        
        [JsonPropertyName("id")]
        public string Id { get; set; }
        
        [JsonPropertyName("name")]
        public string Name { get; set; }
        
        [JsonPropertyName("input")]
        public object Input { get; set; } // Tool arguments as JSON object
        
        [JsonPropertyName("tool_use_id")]
        public string ToolUseId { get; set; }
        
        [JsonPropertyName("content")]
        public string ToolResultContent { get; set; }
    }
    
    public class AnthropicTool
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }
        
        [JsonPropertyName("description")]
        public string Description { get; set; }
        
        [JsonPropertyName("input_schema")]
        public object InputSchema { get; set; } // JSON Schema object
    }
}