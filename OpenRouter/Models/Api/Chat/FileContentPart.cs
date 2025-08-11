using System.Text.Json.Serialization;

namespace Saturn.OpenRouter.Models.Api.Chat
{
    /// <summary>
    /// File content part (e.g., PDF) for multimodal messages.
    /// </summary>
    public sealed class FileContentPart
    {
        /// <summary>Type discriminator: "file".</summary>
        [JsonPropertyName("type")]
        public string Type { get; set; } = "file";

        /// <summary>File descriptor; either a URL or a base64 data URL.</summary>
        [JsonPropertyName("file")]
        public FileData? File { get; set; }

        /// <summary>Nested object describing the file payload.</summary>
        public sealed class FileData
        {
            /// <summary>Publicly accessible URL to the file.</summary>
            [JsonPropertyName("url")]
            public string? Url { get; set; }

            /// <summary>Base64 Data URL for local/inline content.</summary>
            [JsonPropertyName("data")]
            public string? Data { get; set; }
        }
    }
}
