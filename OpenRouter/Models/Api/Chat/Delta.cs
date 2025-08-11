using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Saturn.OpenRouter.Models.Api.Chat
{
    /// <summary>
    /// Delta payload used in streaming responses. Fields are partial/incremental.
    /// </summary>
    public sealed class Delta
    {
        /// <summary>Optional role field streamed at the start.</summary>
        [JsonPropertyName("role")]
        public string? Role { get; set; }

        /// <summary>Incremental content token(s), may be null.</summary>
        [JsonPropertyName("content")]
        public string? Content { get; set; }

        /// <summary>Partial tool calls if requested by the model.</summary>
        [JsonPropertyName("tool_calls")]
        public ToolCall[]? ToolCalls { get; set; }

        /// <summary>Partial annotations when available.</summary>
        [JsonPropertyName("annotations")]
        public List<Annotation>? Annotations { get; set; }
    }
}
