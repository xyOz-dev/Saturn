using System.Text.Json.Serialization;
using Saturn.OpenRouter.Models.Api.Common;

namespace Saturn.OpenRouter.Models.Api.Completions
{
    /// <summary>
    /// Text-only completion response.
    /// </summary>
    public sealed class TextCompletionResponse
    {
        /// <summary>Generation id.</summary>
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        /// <summary>Object, e.g., "text_completion".</summary>
        [JsonPropertyName("object")]
        public string? Object { get; set; }

        /// <summary>Unix timestamp (seconds).</summary>
        [JsonPropertyName("created")]
        public long? Created { get; set; }

        /// <summary>Final model used.</summary>
        [JsonPropertyName("model")]
        public string? Model { get; set; }

        /// <summary>Choices with text payload.</summary>
        [JsonPropertyName("choices")]
        public TextChoice[]? Choices { get; set; }

        /// <summary>Usage accounting details.</summary>
        [JsonPropertyName("usage")]
        public ResponseUsage? Usage { get; set; }
    }
}
