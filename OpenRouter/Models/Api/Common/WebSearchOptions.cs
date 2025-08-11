using System.Text.Json.Serialization;

namespace Saturn.OpenRouter.Models.Api.Common
{
    /// <summary>
    /// Options for built-in web search on certain models (non-plugin mode).
    /// </summary>
    public sealed class WebSearchOptions
    {
        /// <summary>
        /// Search context size: "low" | "medium" | "high".
        /// Determines how much search context is retrieved.
        /// </summary>
        [JsonPropertyName("search_context_size")]
        public string? SearchContextSize { get; set; }
    }
}
