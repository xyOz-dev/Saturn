using System.Text.Json.Serialization;
using Saturn.OpenRouter.Models.Api.Common;

namespace Saturn.OpenRouter.Models.Api.Chat
{
    /// <summary>
    /// Choice container for non-streaming responses.
    /// </summary>
    public sealed class Choice
    {
        /// <summary>Assistant message returned for this choice.</summary>
        [JsonPropertyName("message")]
        public AssistantMessageResponse? Message { get; set; }

        /// <summary>Normalized finish reason (e.g., "stop","tool_calls","length","error").</summary>
        [JsonPropertyName("finish_reason")]
        public string? FinishReason { get; set; }

        /// <summary>Provider-native finish reason string.</summary>
        [JsonPropertyName("native_finish_reason")]
        public string? NativeFinishReason { get; set; }

        /// <summary>Optional error at choice-level (zero completion insurance scenarios, etc.).</summary>
        [JsonPropertyName("error")]
        public ResponseError? Error { get; set; }
    }
}
