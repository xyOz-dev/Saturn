using System.Text.Json.Serialization;

namespace Saturn.OpenRouter.Models.Api.Common
{
    public sealed class WebSearchOptions
    {
        [JsonPropertyName("search_context_size")]
        public string? SearchContextSize { get; set; }
    }
}
