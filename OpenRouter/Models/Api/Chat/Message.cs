using System.Text.Json;
using System.Text.Json.Serialization;

namespace Saturn.OpenRouter.Models.Api.Chat
{
    /// <summary>
    /// Chat message. role: "user" | "assistant" | "system" | "tool".
    /// content is string or array of content parts for multimodal inputs (user role).
    /// </summary>
    public sealed class Message
    {
        /// <summary>Role of the message.</summary>
        [JsonPropertyName("role")]
        public string? Role { get; set; }

        /// <summary>
        /// Content string or array of content parts.
        /// When user role, may be an array of content parts (text, image_url, file, input_audio).
        /// </summary>
        [JsonPropertyName("content")]
        public JsonElement Content { get; set; }

        /// <summary>Optional display name. For non-OpenAI providers may be prepended to content.</summary>
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        /// <summary>When role="tool", the associated tool call id.</summary>
        [JsonPropertyName("tool_call_id")]
        public string? ToolCallId { get; set; }

        /// <summary>
        /// When role="assistant" and the assistant is requesting tool calls,
        /// this array carries the tool call requests (OpenAI-compatible).
        /// </summary>
        [JsonPropertyName("tool_calls")]
        public ToolCallRequest[]? ToolCalls { get; set; }
    }
}
