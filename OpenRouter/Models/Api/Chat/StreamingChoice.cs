using System.Text.Json.Serialization;
using Saturn.OpenRouter.Models.Api.Common;

namespace Saturn.OpenRouter.Models.Api.Chat
{
    /// <summary>
    /// Streaming choice delta with optional finish reason and error at the choice level.
    /// </summary>
    public sealed class StreamingChoice
    {
        /// <summary>Delta payload with token increments, tool calls, etc.</summary>
        [JsonPropertyName("delta")]
        public Delta? Delta { get; set; }

        /// <summary>Normalized finish reason, if determined during streaming.</summary>
        [JsonPropertyName("finish_reason")]
        public string? FinishReason { get; set; }

        /// <summary>Provider-native finish reason.</summary>
        [JsonPropertyName("native_finish_reason")]
        public string? NativeFinishReason { get; set; }

        /// <summary>Streaming error info if any.</summary>
        [JsonPropertyName("error")]
        public ResponseError? Error { get; set; }
    }
}
