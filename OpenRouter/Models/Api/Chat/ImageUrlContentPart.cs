using System.Text.Json.Serialization;

namespace Saturn.OpenRouter.Models.Api.Chat
{
    public sealed class ImageUrlContentPart
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "image_url";

        [JsonPropertyName("image_url")]
        public ImageUrlData? ImageUrl { get; set; }

        public sealed class ImageUrlData
        {
            [JsonPropertyName("url")]
            public string? Url { get; set; }

            [JsonPropertyName("detail")]
            public string? Detail { get; set; }
        }
    }
}
