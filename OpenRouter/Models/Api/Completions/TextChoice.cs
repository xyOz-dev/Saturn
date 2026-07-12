using System.Text.Json.Serialization;
using Saturn.OpenRouter.Models.Api.Common;

namespace Saturn.OpenRouter.Models.Api.Completions
{
    public sealed class TextChoice
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; }

        [JsonPropertyName("finish_reason")]
        public string? FinishReason { get; set; }

        [JsonPropertyName("error")]
        public ResponseError? Error { get; set; }
    }
}
