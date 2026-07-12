using System.Collections.Generic;
using System.Text.Json.Serialization;
using Saturn.OpenRouter.Models.Api.Common;
using Saturn.OpenRouter.Models.Api.Common.Plugins;

namespace Saturn.OpenRouter.Models.Api.Completions
{
    public sealed class TextCompletionRequest
    {
        [JsonPropertyName("model")]
        public string? Model { get; set; }

        [JsonPropertyName("prompt")]
        public string? Prompt { get; set; }

        [JsonPropertyName("max_tokens")]
        public int? MaxTokens { get; set; }

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

        [JsonPropertyName("logit_bias")]
        public Dictionary<int, double>? LogitBias { get; set; }

        [JsonPropertyName("stop")]
        public object? Stop { get; set; }

        [JsonPropertyName("provider")]
        public ProviderPreferences? Provider { get; set; }

        [JsonPropertyName("transforms")]
        public string[]? Transforms { get; set; }

        [JsonPropertyName("usage")]
        public UsageOption? Usage { get; set; }

        [JsonPropertyName("plugins")]
        public PluginWrapper[]? Plugins { get; set; }

        [JsonPropertyName("user")]
        public string? User { get; set; }
    }
}
