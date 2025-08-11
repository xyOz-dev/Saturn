using System.Collections.Generic;
using System.Text.Json.Serialization;
using Saturn.OpenRouter.Models.Api.Common;
using Saturn.OpenRouter.Models.Api.Common.Plugins;

namespace Saturn.OpenRouter.Models.Api.Completions
{
    /// <summary>
    /// Text-only completions request (OpenAI-compatible) with OpenRouter extensions.
    /// </summary>
    public sealed class TextCompletionRequest
    {
        /// <summary>Target model.</summary>
        [JsonPropertyName("model")]
        public string? Model { get; set; }

        /// <summary>Prompt string.</summary>
        [JsonPropertyName("prompt")]
        public string? Prompt { get; set; }

        /// <summary>Maximum tokens to generate.</summary>
        [JsonPropertyName("max_tokens")]
        public int? MaxTokens { get; set; }

        // Sampling params at root
        [JsonPropertyName("temperature")] public double? Temperature { get; set; }
        [JsonPropertyName("top_p")] public double? TopP { get; set; }
        [JsonPropertyName("top_k")] public int? TopK { get; set; }
        [JsonPropertyName("frequency_penalty")] public double? FrequencyPenalty { get; set; }
        [JsonPropertyName("presence_penalty")] public double? PresencePenalty { get; set; }
        [JsonPropertyName("repetition_penalty")] public double? RepetitionPenalty { get; set; }
        [JsonPropertyName("logprobs")] public bool? Logprobs { get; set; }
        [JsonPropertyName("top_logprobs")] public int? TopLogprobs { get; set; }
        [JsonPropertyName("min_p")] public double? MinP { get; set; }
        [JsonPropertyName("top_a")] public double? TopA { get; set; }
        [JsonPropertyName("seed")] public int? Seed { get; set; }

        /// <summary>Bias for specific token ids.</summary>
        [JsonPropertyName("logit_bias")]
        public Dictionary<int, double>? LogitBias { get; set; }

        /// <summary>Stop sequences (string or array).</summary>
        [JsonPropertyName("stop")]
        public object? Stop { get; set; }

        /// <summary>Provider routing preferences.</summary>
        [JsonPropertyName("provider")]
        public ProviderPreferences? Provider { get; set; }

        /// <summary>Transforms to apply (e.g., "middle-out").</summary>
        [JsonPropertyName("transforms")]
        public string[]? Transforms { get; set; }

        /// <summary>Include usage accounting details.</summary>
        [JsonPropertyName("usage")]
        public UsageOption? Usage { get; set; }

        /// <summary>Plugins to apply to this request (rare for completions).</summary>
        [JsonPropertyName("plugins")]
        public PluginWrapper[]? Plugins { get; set; }

        /// <summary>Stable identifier for end-user.</summary>
        [JsonPropertyName("user")]
        public string? User { get; set; }
    }
}
