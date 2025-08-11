using System.Text.Json.Serialization;

namespace Saturn.OpenRouter.Models.Api.Chat
{
    /// <summary>
    /// Image content part for multimodal user messages (URL or base64 data).
    /// </summary>
    public sealed class ImageUrlContentPart
    {
        /// <summary>Type discriminator: "image_url".</summary>
        [JsonPropertyName("type")]
        public string Type { get; set; } = "image_url";

        /// <summary>Image descriptor containing URL and optional detail.</summary>
        [JsonPropertyName("image_url")]
        public ImageUrlData? ImageUrl { get; set; }

        /// <summary>Nested object for image url payload.</summary>
        public sealed class ImageUrlData
        {
            /// <summary>Image URL or base64 data URL.</summary>
            [JsonPropertyName("url")]
            public string? Url { get; set; }

            /// <summary>Optional detail level (provider-specific; often "auto").</summary>
            [JsonPropertyName("detail")]
            public string? Detail { get; set; }
        }
    }
}
