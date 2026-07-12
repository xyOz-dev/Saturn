using System.Text.Json.Serialization;

namespace Saturn.OpenRouter.Models.Api.Common
{
    public sealed class ReasoningConfig
    {
        [JsonPropertyName("effort")]
        public string? Effort { get; set; }

        [JsonPropertyName("max_tokens")]
        public int? MaxTokens { get; set; }

        [JsonPropertyName("exclude")]
        public bool? Exclude { get; set; }

        [JsonPropertyName("enabled")]
        public bool? Enabled { get; set; }
    }
}
