using System.Text.Json.Serialization;

namespace Saturn.OpenRouter.Models.Api.Chat
{
    /// <summary>
    /// Tool definition wrapper (OpenAI-compatible).
    /// </summary>
    public sealed class ToolDefinition
    {
        /// <summary>Type discriminator; must be "function".</summary>
        [JsonPropertyName("type")]
        public string Type { get; set; } = "function";

        /// <summary>Function details.</summary>
        [JsonPropertyName("function")]
        public ToolFunction? Function { get; set; }
    }
}
