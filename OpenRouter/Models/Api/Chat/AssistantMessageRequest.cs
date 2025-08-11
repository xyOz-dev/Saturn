using System.Text.Json.Serialization;

namespace Saturn.OpenRouter.Models.Api.Chat
{
    /// <summary>
    /// Assistant prefill or tool-call wrapper when sending follow-up with tool results.
    /// </summary>
    public sealed class AssistantMessageRequest
    {
        /// <summary>Role must be "assistant".</summary>
        [JsonPropertyName("role")]
        public string Role { get; set; } = "assistant";

        /// <summary>Optional prefilled content (or null when emitting tool_calls only).</summary>
        [JsonPropertyName("content")]
        public string? Content { get; set; }

        /// <summary>Tool calls requested by the model in previous turn.</summary>
        [JsonPropertyName("tool_calls")]
        public ToolCallRequest[]? ToolCalls { get; set; }
    }
}
