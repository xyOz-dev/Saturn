using System.Text.Json.Serialization;

namespace Saturn.OpenRouter.Models.Api.Common
{
    public sealed class MaxPrice
    {
        [JsonPropertyName("prompt")]
        public decimal? Prompt { get; set; }

        [JsonPropertyName("completion")]
        public decimal? Completion { get; set; }

        [JsonPropertyName("request")]
        public decimal? Request { get; set; }

        [JsonPropertyName("image")]
        public decimal? Image { get; set; }
    }
}
