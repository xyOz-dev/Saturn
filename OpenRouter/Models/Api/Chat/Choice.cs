using System.Text.Json.Serialization;
using Saturn.OpenRouter.Models.Api.Common;

namespace Saturn.OpenRouter.Models.Api.Chat
{
    public sealed class Choice
    {
        [JsonPropertyName("message")]
        public AssistantMessageResponse? Message { get; set; }

        [JsonPropertyName("finish_reason")]
        public string? FinishReason { get; set; }

        [JsonPropertyName("native_finish_reason")]
        public string? NativeFinishReason { get; set; }

        [JsonPropertyName("error")]
        public ResponseError? Error { get; set; }
    }
}
