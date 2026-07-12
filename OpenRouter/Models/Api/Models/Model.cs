using System.Text.Json;
using System.Text.Json.Serialization;

namespace Saturn.OpenRouter.Models.Api.Models
{
    public sealed class Model
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("canonical_slug")]
        public string? CanonicalSlug { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("created")]
        public long? Created { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("context_length")]
        public int? ContextLength { get; set; }

        [JsonPropertyName("architecture")]
        public ArchitectureInfo? Architecture { get; set; }

        [JsonPropertyName("pricing")]
        public PricingInfo? Pricing { get; set; }

        [JsonPropertyName("top_provider")]
        public TopProviderInfo? TopProvider { get; set; }

        [JsonPropertyName("per_request_limits")]
        public Dictionary<string, JsonElement>? PerRequestLimits { get; set; }

        [JsonPropertyName("supported_parameters")]
        public string[]? SupportedParameters { get; set; }
    }
}