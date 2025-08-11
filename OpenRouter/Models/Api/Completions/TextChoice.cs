using System.Text.Json.Serialization;
using Saturn.OpenRouter.Models.Api.Common;

namespace Saturn.OpenRouter.Models.Api.Completions
{
    /// <summary>
    /// Choice for text-only completions.
    /// </summary>
    public sealed class TextChoice
    {
        /// <summary>Generated text.</summary>
        [JsonPropertyName("text")]
        public string? Text { get; set; }

        /// <summary>Finish reason string (normalized).</summary>
        [JsonPropertyName("finish_reason")]
        public string? FinishReason { get; set; }

        /// <summary>Optional error at choice level.</summary>
        [JsonPropertyName("error")]
        public ResponseError? Error { get; set; }
    }
}
