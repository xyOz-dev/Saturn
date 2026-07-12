using System.Text.Json.Serialization;

namespace Saturn.OpenRouter.Models.Api.Chat
{
    public sealed class UrlCitationAnnotation
    {
        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("content")]
        public string? Content { get; set; }

        [JsonPropertyName("start_index")]
        public int? StartIndex { get; set; }

        [JsonPropertyName("end_index")]
        public int? EndIndex { get; set; }
    }
}
