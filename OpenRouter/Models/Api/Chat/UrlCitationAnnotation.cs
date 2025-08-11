using System.Text.Json.Serialization;

namespace Saturn.OpenRouter.Models.Api.Chat
{
    /// <summary>
    /// Details for url citation annotations attached to assistant messages.
    /// </summary>
    public sealed class UrlCitationAnnotation
    {
        /// <summary>URL to the cited source.</summary>
        [JsonPropertyName("url")]
        public string? Url { get; set; }

        /// <summary>Title of the source.</summary>
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        /// <summary>Optional content snippet extracted from the source.</summary>
        [JsonPropertyName("content")]
        public string? Content { get; set; }

        /// <summary>Inclusive start index of the citation span within the message content.</summary>
        [JsonPropertyName("start_index")]
        public int? StartIndex { get; set; }

        /// <summary>Inclusive end index of the citation span within the message content.</summary>
        [JsonPropertyName("end_index")]
        public int? EndIndex { get; set; }
    }
}
