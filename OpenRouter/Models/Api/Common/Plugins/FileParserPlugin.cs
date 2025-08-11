using System.Text.Json.Serialization;

namespace Saturn.OpenRouter.Models.Api.Common.Plugins
{
    /// <summary>
    /// File parser plugin configuration (id="file-parser"), used for PDF processing.
    /// </summary>
    public sealed class FileParserPlugin
    {
        /// <summary>Plugin id. Always "file-parser" for this plugin.</summary>
        [JsonPropertyName("id")]
        public string Id { get; set; } = "file-parser";

        /// <summary>PDF-specific options such as engine selection.</summary>
        [JsonPropertyName("pdf")]
        public PdfOptions? Pdf { get; set; }

        /// <summary>Nested PDF options.</summary>
        public sealed class PdfOptions
        {
            /// <summary>
            /// PDF engine: "pdf-text" (free), "mistral-ocr" (OCR), or "native" (model-native file input).
            /// </summary>
            [JsonPropertyName("engine")]
            public string? Engine { get; set; }
        }
    }
}
