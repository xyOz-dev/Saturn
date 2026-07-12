using System.Text.Json;
using System.Text.Json.Serialization;

namespace Saturn.OpenRouter.Models.Api.Chat
{
    public sealed class Message
    {
        [JsonPropertyName("role")]
        public string? Role { get; set; }

        [JsonPropertyName("content")]
        public JsonElement Content { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("tool_call_id")]
        public string? ToolCallId { get; set; }

        [JsonPropertyName("tool_calls")]
        public ToolCallRequest[]? ToolCalls { get; set; }
    }
}
