using System.Text.Json.Serialization;
using Saturn.OpenRouter.Models.Api.Common;

namespace Saturn.OpenRouter.Models.Api.Completions
{
    public sealed class TextCompletionResponse
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("object")]
        public string? Object { get; set; }

        [JsonPropertyName("created")]
        public long? Created { get; set; }

        [JsonPropertyName("model")]
        public string? Model { get; set; }

        [JsonPropertyName("choices")]
        public TextChoice[]? Choices { get; set; }

        [JsonPropertyName("usage")]
        public ResponseUsage? Usage { get; set; }
    }
}
