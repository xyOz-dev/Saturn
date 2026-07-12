using System.Text.Json.Serialization;

namespace Saturn.OpenRouter.Models.Api.Common
{
    public sealed class ResponseUsage
    {
        [JsonPropertyName("prompt_tokens")]
        public int? PromptTokens { get; set; }

        [JsonPropertyName("completion_tokens")]
        public int? CompletionTokens { get; set; }

        [JsonPropertyName("total_tokens")]
        public int? TotalTokens { get; set; }

        [JsonPropertyName("prompt_tokens_details")]
        public PromptTokensDetailsInfo? PromptTokensDetails { get; set; }

        [JsonPropertyName("completion_tokens_details")]
        public CompletionTokensDetailsInfo? CompletionTokensDetails { get; set; }

        [JsonPropertyName("cost")]
        public decimal? Cost { get; set; }

        [JsonPropertyName("cost_details")]
        public CostDetailsInfo? CostDetails { get; set; }

        public sealed class PromptTokensDetailsInfo
        {
            [JsonPropertyName("cached_tokens")]
            public int? CachedTokens { get; set; }
        }

        public sealed class CompletionTokensDetailsInfo
        {
            [JsonPropertyName("reasoning_tokens")]
            public int? ReasoningTokens { get; set; }
        }

        public sealed class CostDetailsInfo
        {
            [JsonPropertyName("upstream_inference_cost")]
            public decimal? UpstreamInferenceCost { get; set; }
        }
    }
}
