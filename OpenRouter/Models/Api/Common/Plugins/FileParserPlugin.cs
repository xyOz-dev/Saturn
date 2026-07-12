using System.Text.Json.Serialization;

namespace Saturn.OpenRouter.Models.Api.Common.Plugins
{
    public sealed class FileParserPlugin
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "file-parser";

        [JsonPropertyName("pdf")]
        public PdfOptions? Pdf { get; set; }

        public sealed class PdfOptions
        {
            [JsonPropertyName("engine")]
            public string? Engine { get; set; }
        }
    }
}
