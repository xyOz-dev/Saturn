using System.Text.Json;
using System.Text.Json.Serialization;

namespace Saturn.OpenRouter.Models.Api
{
    /// <summary>
    /// Strongly-typed error response shape returned by OpenRouter:
    /// { "error": { "code": number, "message": string, "metadata": object? } }
    /// </summary>
    public sealed class ErrorResponse
    {
        /// <summary>The error object containing details.</summary>
        [JsonPropertyName("error")]
        public ErrorBody? Error { get; set; }

        /// <summary>
        /// Inner error body details.
        /// </summary>
        public sealed class ErrorBody
        {
            /// <summary>Numeric API-specific error code, when provided.</summary>
            [JsonPropertyName("code")]
            public int? Code { get; set; }

            /// <summary>Human-readable error message.</summary>
            [JsonPropertyName("message")]
            public string? Message { get; set; }

            /// <summary>Optional metadata bag.</summary>
            [JsonPropertyName("metadata")]
            public Dictionary<string, JsonElement>? Metadata { get; set; }
        }
    }
}