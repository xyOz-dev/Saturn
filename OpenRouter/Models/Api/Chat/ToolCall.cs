using System.Text.Json.Serialization;

namespace Saturn.OpenRouter.Models.Api.Chat
{
    /// <summary>
    /// Tool call in assistant message responses.
    /// </summary>
    public sealed class ToolCall
    {
        /// <summary>Unique call id.</summary>
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        /// <summary>Type discriminator; "function".</summary>
        [JsonPropertyName("type")]
        public string Type { get; set; } = "function";

        /// <summary>Function call payload.</summary>
        [JsonPropertyName("function")]
        public FunctionCall? Function { get; set; }

        /// <summary>Function call details.</summary>
        public sealed class FunctionCall
        {
            /// <summary>Function name.</summary>
            [JsonPropertyName("name")]
            public string? Name { get; set; }

            /// <summary>Stringified JSON arguments.</summary>
            [JsonPropertyName("arguments")]
            public string? Arguments { get; set; }
        }
    }
}
