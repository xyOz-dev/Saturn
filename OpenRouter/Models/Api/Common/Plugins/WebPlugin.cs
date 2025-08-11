using System.Text.Json.Serialization;

namespace Saturn.OpenRouter.Models.Api.Common.Plugins
{
    /// <summary>
    /// Web search plugin configuration (id="web").
    /// </summary>
    public sealed class WebPlugin
    {
        /// <summary>Plugin id. Always "web" for this plugin.</summary>
        [JsonPropertyName("id")]
        public string Id { get; set; } = "web";

        /// <summary>Maximum number of results to include. Defaults to 5 if not provided.</summary>
        [JsonPropertyName("max_results")]
        public int? MaxResults { get; set; }

        /// <summary>Optional prompt prefix used when attaching search results.</summary>
        [JsonPropertyName("search_prompt")]
        public string? SearchPrompt { get; set; }
    }
}
