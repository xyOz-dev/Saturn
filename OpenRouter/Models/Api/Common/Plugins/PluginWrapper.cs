using System.Text.Json.Serialization;

namespace Saturn.OpenRouter.Models.Api.Common.Plugins
{
    public sealed class PluginWrapper
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("max_results")]
        public int? MaxResults { get; set; }

        [JsonPropertyName("search_prompt")]
        public string? SearchPrompt { get; set; }

        [JsonPropertyName("pdf")]
        public FileParserPlugin.PdfOptions? Pdf { get; set; }
    }
}
