using System.Text.Json.Serialization;
using Saturn.OpenRouter.Models.Api.Common;

namespace Saturn.OpenRouter.Models.Api.Chat
{
    /// <summary>
    /// Non-streaming chat completion response.
    /// </summary>
    public sealed class ChatCompletionResponse
    {
        /// <summary>Generation id.</summary>
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        /// <summary>Unix timestamp (seconds).</summary>
        [JsonPropertyName("created")]
        public long? Created { get; set; }

        /// <summary>Final model used.</summary>
        [JsonPropertyName("model")]
        public string? Model { get; set; }

        /// <summary>Object type, "chat.completion".</summary>
        [JsonPropertyName("object")]
        public string? Object { get; set; }

        /// <summary>Choices array with assistant messages.</summary>
        [JsonPropertyName("choices")]
        public Choice[]? Choices { get; set; }

        /// <summary>Usage accounting.</summary>
        [JsonPropertyName("usage")]
        public ResponseUsage? Usage { get; set; }

        /// <summary>Optional provider/system fingerprint.</summary>
        [JsonPropertyName("system_fingerprint")]
        public string? SystemFingerprint { get; set; }
    }
}
