using System.Text.Json.Serialization;

namespace Saturn.OpenRouter.Models.Api.Models
{
    /// <summary>
    /// Pricing information for a model. All values are represented as strings as provided by the API.
    /// </summary>
    public sealed class PricingInfo
    {
        /// <summary>Per-token or unit prompt price.</summary>
        [JsonPropertyName("prompt")]
        public string? Prompt { get; set; }

        /// <summary>Per-token or unit completion price.</summary>
        [JsonPropertyName("completion")]
        public string? Completion { get; set; }

        /// <summary>Per-request base price.</summary>
        [JsonPropertyName("request")]
        public string? Request { get; set; }

        /// <summary>Image-related pricing.</summary>
        [JsonPropertyName("image")]
        public string? Image { get; set; }

        /// <summary>Web search pricing.</summary>
        [JsonPropertyName("web_search")]
        public string? WebSearch { get; set; }

        /// <summary>Internal reasoning pricing.</summary>
        [JsonPropertyName("internal_reasoning")]
        public string? InternalReasoning { get; set; }

        /// <summary>Input cache read pricing.</summary>
        [JsonPropertyName("input_cache_read")]
        public string? InputCacheRead { get; set; }

        /// <summary>Input cache write pricing.</summary>
        [JsonPropertyName("input_cache_write")]
        public string? InputCacheWrite { get; set; }
    }
}