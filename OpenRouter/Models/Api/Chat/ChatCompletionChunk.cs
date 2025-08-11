using System.Text.Json.Serialization;
using Saturn.OpenRouter.Models.Api.Common;

namespace Saturn.OpenRouter.Models.Api.Chat
{
    /// <summary>
    /// Streaming chat completion chunk ("chat.completion.chunk") with delta tokens.
    /// </summary>
    public sealed class ChatCompletionChunk
    {
        /// <summary>Generation id.</summary>
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        /// <summary>Unix timestamp (seconds).</summary>
        [JsonPropertyName("created")]
        public long? Created { get; set; }

        /// <summary>Model producing this chunk.</summary>
        [JsonPropertyName("model")]
        public string? Model { get; set; }

        /// <summary>Object type, "chat.completion.chunk".</summary>
        [JsonPropertyName("object")]
        public string? Object { get; set; }

        /// <summary>Streaming choice deltas.</summary>
        [JsonPropertyName("choices")]
        public StreamingChoice[]? Choices { get; set; }

        /// <summary>Usage object included at the end of the stream.</summary>
        [JsonPropertyName("usage")]
        public ResponseUsage? Usage { get; set; }
    }
}
