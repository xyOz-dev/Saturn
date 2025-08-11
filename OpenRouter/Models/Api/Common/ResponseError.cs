using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Saturn.OpenRouter.Models.Api.Common
{
    /// <summary>
    /// Normalized error shape emitted in choice-level streaming and non-streaming responses.
    /// Distinct from the top-level <see cref="Saturn.OpenRouter.Models.Api.ErrorResponse"/>.
    /// </summary>
    public sealed class ResponseError
    {
        /// <summary>Numeric error code when provided.</summary>
        [JsonPropertyName("code")]
        public int? Code { get; set; }

        /// <summary>Human-readable error message.</summary>
        [JsonPropertyName("message")]
        public string? Message { get; set; }

        /// <summary>Additional metadata bag (e.g., provider details, raw error).</summary>
        [JsonPropertyName("metadata")]
        public Dictionary<string, JsonElement>? Metadata { get; set; }
    }
}
