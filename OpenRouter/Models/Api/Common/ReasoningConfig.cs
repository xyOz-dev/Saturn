using System.Text.Json.Serialization;

namespace Saturn.OpenRouter.Models.Api.Common
{
    /// <summary>
    /// Unified reasoning configuration across providers.
    /// </summary>
    public sealed class ReasoningConfig
    {
        /// <summary>
        /// Reasoning "effort" level (e.g., "low" | "medium" | "high").
        /// For providers that use effort levels.
        /// </summary>
        [JsonPropertyName("effort")]
        public string? Effort { get; set; }

        /// <summary>
        /// Maximum number of reasoning tokens to allocate (for models that support direct token allocation).
        /// </summary>
        [JsonPropertyName("max_tokens")]
        public int? MaxTokens { get; set; }

        /// <summary>Exclude reasoning blocks from the returned message while still using them internally.</summary>
        [JsonPropertyName("exclude")]
        public bool? Exclude { get; set; }

        /// <summary>Enable reasoning with default parameters (provider-dependent).</summary>
        [JsonPropertyName("enabled")]
        public bool? Enabled { get; set; }
    }
}
