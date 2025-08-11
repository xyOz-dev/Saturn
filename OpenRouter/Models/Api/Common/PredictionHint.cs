using System.Text.Json.Serialization;

namespace Saturn.OpenRouter.Models.Api.Common
{
    /// <summary>
    /// Hint to reduce latency by providing a predicted output the model may continue.
    /// </summary>
    public sealed class PredictionHint
    {
        /// <summary>Type discriminator; for now only "content".</summary>
        [JsonPropertyName("type")]
        public string Type { get; set; } = "content";

        /// <summary>The predicted content to bias towards.</summary>
        [JsonPropertyName("content")]
        public string? Content { get; set; }
    }
}
