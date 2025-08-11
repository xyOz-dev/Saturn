using System.Text.Json.Serialization;

namespace Saturn.OpenRouter.Models.Api.Common
{
    /// <summary>
    /// Maximum price caps per unit you will accept for routing.
    /// Values are modeled as nullable decimal in USD for flexibility/precision.
    /// </summary>
    public sealed class MaxPrice
    {
        /// <summary>Max cost per input token (USD/million tokens equivalent).</summary>
        [JsonPropertyName("prompt")]
        public decimal? Prompt { get; set; }

        /// <summary>Max cost per output token (USD/million tokens equivalent).</summary>
        [JsonPropertyName("completion")]
        public decimal? Completion { get; set; }

        /// <summary>Max fixed cost per request (USD per request).</summary>
        [JsonPropertyName("request")]
        public decimal? Request { get; set; }

        /// <summary>Max cost per image (USD per image).</summary>
        [JsonPropertyName("image")]
        public decimal? Image { get; set; }
    }
}
