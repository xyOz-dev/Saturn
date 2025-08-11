using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Saturn.OpenRouter.Models.Api.Common
{
    /// <summary>
    /// Common sampling parameters supported by various providers/models.
    /// </summary>
    public sealed class SamplingParameters
    {
        [JsonPropertyName("temperature")]
        public double? Temperature { get; set; }

        [JsonPropertyName("top_p")]
        public double? TopP { get; set; }

        [JsonPropertyName("top_k")]
        public int? TopK { get; set; }

        [JsonPropertyName("frequency_penalty")]
        public double? FrequencyPenalty { get; set; }

        [JsonPropertyName("presence_penalty")]
        public double? PresencePenalty { get; set; }

        [JsonPropertyName("repetition_penalty")]
        public double? RepetitionPenalty { get; set; }

        /// <summary>Bias for specific token ids. Keys are token IDs.</summary>
        [JsonPropertyName("logit_bias")]
        public Dictionary<int, double>? LogitBias { get; set; }

        /// <summary>Whether to return log probabilities of output tokens.</summary>
        [JsonPropertyName("logprobs")]
        public bool? Logprobs { get; set; }

        /// <summary>Number of top tokens to include per position when logprobs==true.</summary>
        [JsonPropertyName("top_logprobs")]
        public int? TopLogprobs { get; set; }

        [JsonPropertyName("min_p")]
        public double? MinP { get; set; }

        [JsonPropertyName("top_a")]
        public double? TopA { get; set; }

        [JsonPropertyName("seed")]
        public int? Seed { get; set; }

        /// <summary>Custom stop sequences. Accepts a string or array of strings.</summary>
        [JsonPropertyName("stop")]
        public object? Stop { get; set; } // string | string[]
    }
}
