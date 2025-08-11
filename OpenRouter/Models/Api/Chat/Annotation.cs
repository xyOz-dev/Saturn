using System.Text.Json.Serialization;

namespace Saturn.OpenRouter.Models.Api.Chat
{
    /// <summary>
    /// Annotation union root. Initial support for type="url_citation".
    /// </summary>
    public sealed class Annotation
    {
        /// <summary>Annotation type, currently "url_citation".</summary>
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        /// <summary>Payload for url citation annotations.</summary>
        [JsonPropertyName("url_citation")]
        public UrlCitationAnnotation? UrlCitation { get; set; }
    }
}
