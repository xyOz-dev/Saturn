using System.Text.Json.Serialization;

namespace Saturn.OpenRouter.Models.Api.Chat
{
    public sealed class Annotation
    {
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("url_citation")]
        public UrlCitationAnnotation? UrlCitation { get; set; }
    }
}
