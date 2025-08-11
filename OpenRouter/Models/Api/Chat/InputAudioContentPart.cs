using System.Text.Json.Serialization;

namespace Saturn.OpenRouter.Models.Api.Chat
{
    /// <summary>
    /// Audio input content part (base64-encoded audio).
    /// </summary>
    public sealed class InputAudioContentPart
    {
        /// <summary>Type discriminator: "input_audio".</summary>
        [JsonPropertyName("type")]
        public string Type { get; set; } = "input_audio";

        /// <summary>Audio descriptor with format and data.</summary>
        [JsonPropertyName("input_audio")]
        public InputAudioData? InputAudio { get; set; }

        /// <summary>Nested audio payload object.</summary>
        public sealed class InputAudioData
        {
            /// <summary>Audio format (e.g., "wav", "mp3").</summary>
            [JsonPropertyName("format")]
            public string? Format { get; set; }

            /// <summary>Base64-encoded audio data.</summary>
            [JsonPropertyName("data")]
            public string? Data { get; set; }
        }
    }
}
