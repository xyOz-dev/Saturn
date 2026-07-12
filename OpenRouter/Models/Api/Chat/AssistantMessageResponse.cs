using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Saturn.OpenRouter.Models.Api.Chat
{
    public sealed class AssistantMessageResponse
    {
        [JsonPropertyName("role")]
        public string? Role { get; set; }

        [JsonPropertyName("content")]
        public string? Content { get; set; }

        [JsonPropertyName("tool_calls")]
        public ToolCall[]? ToolCalls { get; set; }

        [JsonPropertyName("annotations")]
        public List<Annotation>? Annotations { get; set; }

        [JsonPropertyName("reasoning")]
        public JsonElement? Reasoning { get; set; }
    }
}
