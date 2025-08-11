using System.Text.Json;
using System.Text.Json.Serialization;

namespace Saturn.OpenRouter.Models.Api.Common
{
    /// <summary>
    /// Provider routing preferences controlling how OpenRouter selects providers.
    /// See docs: Provider Routing.
    /// </summary>
    public sealed class ProviderPreferences
    {
        /// <summary>List of provider slugs to try in order (e.g. ["anthropic","openai"]).</summary>
        [JsonPropertyName("order")]
        public string[]? Order { get; set; }

        /// <summary>Whether to allow backup providers when the primary is unavailable. Default: true.</summary>
        [JsonPropertyName("allow_fallbacks")]
        public bool? AllowFallbacks { get; set; }

        /// <summary>Only use providers that support all parameters in your request. Default: false.</summary>
        [JsonPropertyName("require_parameters")]
        public bool? RequireParameters { get; set; }

        /// <summary>"allow" or "deny" - control whether to use providers that may store data.</summary>
        [JsonPropertyName("data_collection")]
        public string? DataCollection { get; set; }

        /// <summary>List of provider slugs to allow for this request.</summary>
        [JsonPropertyName("only")]
        public string[]? Only { get; set; }

        /// <summary>List of provider slugs to skip for this request.</summary>
        [JsonPropertyName("ignore")]
        public string[]? Ignore { get; set; }

        /// <summary>List of quantization levels to filter by (e.g. ["int4","int8"]).</summary>
        [JsonPropertyName("quantizations")]
        public string[]? Quantizations { get; set; }

        /// <summary>Sort providers by "price", "throughput", or "latency". Disables default load balancing.</summary>
        [JsonPropertyName("sort")]
        public string? Sort { get; set; }

        /// <summary>Maximum pricing caps you will accept for this request.</summary>
        [JsonPropertyName("max_price")]
        public MaxPrice? MaxPrice { get; set; }
    }
}
