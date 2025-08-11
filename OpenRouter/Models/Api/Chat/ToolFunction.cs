using System.Text.Json;
using System.Text.Json.Serialization;

namespace Saturn.OpenRouter.Models.Api.Chat
{
    /// <summary>
    /// Function signature used for tool calling (OpenAI-compatible).
    /// </summary>
    public sealed class ToolFunction
    {
        /// <summary>Function name.</summary>
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        /// <summary>Human-readable description.</summary>
        [JsonPropertyName("description")]
        public string? Description { get; set; }

        /// <summary>JSON Schema parameters object.</summary>
        [JsonPropertyName("parameters")]
        public JsonElement Parameters { get; set; }
    }
}
