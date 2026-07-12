using System.Text.Json.Serialization;

namespace Saturn.OpenRouter.Models.Api.Chat
{
    public sealed class InputAudioContentPart
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "input_audio";

        [JsonPropertyName("input_audio")]
        public InputAudioData? InputAudio { get; set; }

        public sealed class InputAudioData
        {
            [JsonPropertyName("format")]
            public string? Format { get; set; }

            [JsonPropertyName("data")]
            public string? Data { get; set; }
        }
    }
}
