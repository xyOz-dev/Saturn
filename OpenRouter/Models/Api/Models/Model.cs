using System.Text.Json;
using System.Text.Json.Serialization;

namespace Saturn.OpenRouter.Models.Api.Models
{
    /// <summary>
    /// Represents a single model entry from the OpenRouter Models API.
    /// </summary>
    public sealed class Model
    {
        /// <summary>Stable model identifier (e.g., "openai/gpt-4o").</summary>
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        /// <summary>Canonical slug if provided by the API.</summary>
        [JsonPropertyName("canonical_slug")]
        public string? CanonicalSlug { get; set; }

        /// <summary>Human-friendly model name.</summary>
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        /// <summary>Creation timestamp (epoch seconds as provided by the API).</summary>
        [JsonPropertyName("created")]
        public long? Created { get; set; }

        /// <summary>Model description when available.</summary>
        [JsonPropertyName("description")]
        public string? Description { get; set; }

        /// <summary>Supported context length (tokens), if provided.</summary>
        [JsonPropertyName("context_length")]
        public int? ContextLength { get; set; }

        /// <summary>Architecture details.</summary>
        [JsonPropertyName("architecture")]
        public ArchitectureInfo? Architecture { get; set; }

        /// <summary>Pricing information.</summary>
        [JsonPropertyName("pricing")]
        public PricingInfo? Pricing { get; set; }

        /// <summary>Information about the top provider for this model.</summary>
        [JsonPropertyName("top_provider")]
        public TopProviderInfo? TopProvider { get; set; }

        /// <summary>Per-request limits as a flexible bag (shape may vary by model).</summary>
        [JsonPropertyName("per_request_limits")]
        public Dictionary<string, JsonElement>? PerRequestLimits { get; set; }

        /// <summary>The set of supported parameter names for this model, if provided.</summary>
        [JsonPropertyName("supported_parameters")]
        public string[]? SupportedParameters { get; set; }
    }
}