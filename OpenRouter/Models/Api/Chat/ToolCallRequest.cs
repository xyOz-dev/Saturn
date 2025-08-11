using System.Text.Json.Serialization;

namespace Saturn.OpenRouter.Models.Api.Chat
{
    /// <summary>
    /// Tool call entry sent back to the model after executing the tool locally.
    /// </summary>
    public sealed class ToolCallRequest
    {
        /// <summary>Unique tool call id.</summary>
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        /// <summary>Type discriminator; always "function".</summary>
        [JsonPropertyName("type")]
        public string Type { get; set; } = "function";

        /// <summary>Function name and stringified arguments.</summary>
        [JsonPropertyName("function")]
        public FunctionCall? Function { get; set; }

        /// <summary>Function call payload.</summary>
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
