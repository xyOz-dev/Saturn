using System.Text.Json.Serialization;

namespace Saturn.OpenRouter.Models.Api.Common.Plugins
{
    public sealed class WebPlugin
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "web";

        [JsonPropertyName("max_results")]
        public int? MaxResults { get; set; }

        [JsonPropertyName("search_prompt")]
        public string? SearchPrompt { get; set; }
    }
}
