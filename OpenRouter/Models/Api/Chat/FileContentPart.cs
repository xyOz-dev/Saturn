using System.Text.Json.Serialization;

namespace Saturn.OpenRouter.Models.Api.Chat
{
    public sealed class FileContentPart
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "file";

        [JsonPropertyName("file")]
        public FileData? File { get; set; }

        public sealed class FileData
        {
            [JsonPropertyName("url")]
            public string? Url { get; set; }

            [JsonPropertyName("data")]
            public string? Data { get; set; }
        }
    }
}
