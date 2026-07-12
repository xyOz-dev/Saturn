using System.Text.Json.Serialization;

namespace Saturn.OpenRouter.Models.Api.Common
{
    public sealed class PredictionHint
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "content";

        [JsonPropertyName("content")]
        public string? Content { get; set; }
    }
}
