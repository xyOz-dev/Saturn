using System.Text.Json.Serialization;

namespace Saturn.OpenRouter.Models.Api.Common.Plugins
{
    /// <summary>
    /// Heterogeneous plugin wrapper allowing arrays of mixed plugin configs.
    /// The shape is discriminated by <c>id</c>. Known ids: "web", "file-parser".
    /// Properties match the underlying plugin schema to allow direct serialization.
    /// </summary>
    public sealed class PluginWrapper
    {
        /// <summary>Plugin discriminator.</summary>
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        // Web plugin fields
        /// <summary>For id="web": maximum results.</summary>
        [JsonPropertyName("max_results")]
        public int? MaxResults { get; set; }

        /// <summary>For id="web": custom search prompt.</summary>
        [JsonPropertyName("search_prompt")]
        public string? SearchPrompt { get; set; }

        // File parser plugin fields
        /// <summary>For id="file-parser": PDF sub-configuration.</summary>
        [JsonPropertyName("pdf")]
        public FileParserPlugin.PdfOptions? Pdf { get; set; }
    }
}
