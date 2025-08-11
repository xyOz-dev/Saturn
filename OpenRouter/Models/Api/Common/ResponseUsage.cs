using System.Text.Json.Serialization;

namespace Saturn.OpenRouter.Models.Api.Common
{
    /// <summary>
    /// Normalized usage object returned in responses. When streaming, it is emitted
    /// at the end of the stream as part of the final chunk.
    /// </summary>
    public sealed class ResponseUsage
    {
        /// <summary>Total input tokens, including images and tools if any.</summary>
        [JsonPropertyName("prompt_tokens")]
        public int? PromptTokens { get; set; }

        /// <summary>Total output tokens generated (including reasoning tokens when applicable).</summary>
        [JsonPropertyName("completion_tokens")]
        public int? CompletionTokens { get; set; }

        /// <summary>Sum of prompt_tokens and completion_tokens.</summary>
        [JsonPropertyName("total_tokens")]
        public int? TotalTokens { get; set; }

        /// <summary>Optional details for prompt tokens (e.g., cached tokens).</summary>
        [JsonPropertyName("prompt_tokens_details")]
        public PromptTokensDetailsInfo? PromptTokensDetails { get; set; }

        /// <summary>Optional details for completion tokens (e.g., reasoning tokens).</summary>
        [JsonPropertyName("completion_tokens_details")]
        public CompletionTokensDetailsInfo? CompletionTokensDetails { get; set; }

        /// <summary>Total cost charged to the account (USD) when available.</summary>
        [JsonPropertyName("cost")]
        public decimal? Cost { get; set; }

        /// <summary>Additional cost breakdown details when available (e.g., upstream provider cost).</summary>
        [JsonPropertyName("cost_details")]
        public CostDetailsInfo? CostDetails { get; set; }

        /// <summary>Details for prompt token accounting.</summary>
        public sealed class PromptTokensDetailsInfo
        {
            /// <summary>Number of tokens read from cache, if any.</summary>
            [JsonPropertyName("cached_tokens")]
            public int? CachedTokens { get; set; }
        }

        /// <summary>Details for completion token accounting.</summary>
        public sealed class CompletionTokensDetailsInfo
        {
            /// <summary>Number of reasoning/thinking tokens, if provided by the model/provider.</summary>
            [JsonPropertyName("reasoning_tokens")]
            public int? ReasoningTokens { get; set; }
        }

        /// <summary>Cost breakdown details.</summary>
        public sealed class CostDetailsInfo
        {
            /// <summary>Actual upstream provider inference cost (applies to BYOK).</summary>
            [JsonPropertyName("upstream_inference_cost")]
            public decimal? UpstreamInferenceCost { get; set; }
        }
    }
}
