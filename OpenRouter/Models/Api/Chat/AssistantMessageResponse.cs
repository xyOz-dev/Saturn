using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Saturn.OpenRouter.Models.Api.Chat
{
    /// <summary>
    /// Assistant message content and metadata.
    /// </summary>
    public sealed class AssistantMessageResponse
    {
        /// <summary>Role of the message; expected "assistant".</summary>
        [JsonPropertyName("role")]
        public string? Role { get; set; }

        /// <summary>Message content string (may be null when tool calls are emitted).</summary>
        [JsonPropertyName("content")]
        public string? Content { get; set; }

        /// <summary>Tool calls requested by the model, if any.</summary>
        [JsonPropertyName("tool_calls")]
        public ToolCall[]? ToolCalls { get; set; }

        /// <summary>Annotations array (e.g., web search url_citation annotations).</summary>
        [JsonPropertyName("annotations")]
        public List<Annotation>? Annotations { get; set; }

        /// <summary>Opaque reasoning blocks/details when provided by the model.</summary>
        [JsonPropertyName("reasoning")]
        public JsonElement? Reasoning { get; set; }
    }
}
