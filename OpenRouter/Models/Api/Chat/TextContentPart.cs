using System.Text.Json.Serialization;

namespace Saturn.OpenRouter.Models.Api.Chat
{
    /// <summary>
    /// Text content part for multimodal user messages.
    /// </summary>
    public sealed class TextContentPart
    {
        /// <summary>Type discriminator: "text".</summary>
        [JsonPropertyName("type")]
        public string Type { get; set; } = "text";

        /// <summary>Text content.</summary>
        [JsonPropertyName("text")]
        public string? Text { get; set; }

        /// <summary>Optional cache control breakpoint for prompt caching.</summary>
        [JsonPropertyName("cache_control")]
        public CacheControl? CacheControl { get; set; }
    }
}
