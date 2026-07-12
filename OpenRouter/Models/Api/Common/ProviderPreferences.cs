using System.Text.Json;
using System.Text.Json.Serialization;

namespace Saturn.OpenRouter.Models.Api.Common
{
    public sealed class ProviderPreferences
    {
        [JsonPropertyName("order")]
        public string[]? Order { get; set; }

        [JsonPropertyName("allow_fallbacks")]
        public bool? AllowFallbacks { get; set; }

        [JsonPropertyName("require_parameters")]
        public bool? RequireParameters { get; set; }

        [JsonPropertyName("data_collection")]
        public string? DataCollection { get; set; }

        [JsonPropertyName("only")]
        public string[]? Only { get; set; }

        [JsonPropertyName("ignore")]
        public string[]? Ignore { get; set; }

        [JsonPropertyName("quantizations")]
        public string[]? Quantizations { get; set; }

        [JsonPropertyName("sort")]
        public string? Sort { get; set; }

        [JsonPropertyName("max_price")]
        public MaxPrice? MaxPrice { get; set; }
    }
}
