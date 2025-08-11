using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Saturn.OpenRouter.Models.Api.Common;
using Saturn.OpenRouter.Models.Api.Common.Plugins;

namespace Saturn.OpenRouter.Models.Api.Chat
{
    /// <summary>
    /// Chat completions request (OpenAI-compatible) extended with OpenRouter features.
    /// Either messages or prompt must be provided.
    /// </summary>
    public sealed class ChatCompletionRequest
    {
        /// <summary>Messages in the conversation.</summary>
        [JsonPropertyName("messages")]
        public Message[]? Messages { get; set; }

        /// <summary>Fallback to plain prompt string when not using messages.</summary>
        [JsonPropertyName("prompt")]
        public string? Prompt { get; set; }

        /// <summary>Target model or preset reference.</summary>
        [JsonPropertyName("model")]
        public string? Model { get; set; }

        /// <summary>Optional output constraints (json_object or json_schema).</summary>
        [JsonPropertyName("response_format")]
        public ResponseFormat? ResponseFormat { get; set; }

        /// <summary>Stop sequences (string or array).</summary>
        [JsonPropertyName("stop")]
        public object? Stop { get; set; }

        /// <summary>Enable server-sent events streaming.</summary>
        [JsonPropertyName("stream")]
        public bool? Stream { get; set; }

        /// <summary>Maximum tokens to generate.</summary>
        [JsonPropertyName("max_tokens")]
        public int? MaxTokens { get; set; }

        // Sampling params at root (OpenAI-compatible)
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

        /// <summary>Tool/function calling definitions.</summary>
        [JsonPropertyName("tools")]
        public ToolDefinition[]? Tools { get; set; }

        /// <summary>Tool selection control: "none" | "auto" | { type:"function", function:{ name } }.</summary>
        [JsonPropertyName("tool_choice")]
        public ToolChoice? ToolChoice { get; set; }

        /// <summary>Unified reasoning controls.</summary>
        [JsonPropertyName("reasoning")]
        public ReasoningConfig? Reasoning { get; set; }

        /// <summary>Latency optimization hint.</summary>
        [JsonPropertyName("prediction")]
        public PredictionHint? Prediction { get; set; }

        /// <summary>Prompt transforms (e.g., middle-out).</summary>
        [JsonPropertyName("transforms")]
        public string[]? Transforms { get; set; }

        /// <summary>Fallback model list.</summary>
        [JsonPropertyName("models")]
        public string[]? Models { get; set; }

        /// <summary>Routing strategy hint (e.g., "fallback").</summary>
        [JsonPropertyName("route")]
        public string? Route { get; set; }

        /// <summary>Provider routing preferences.</summary>
        [JsonPropertyName("provider")]
        public ProviderPreferences? Provider { get; set; }

        /// <summary>Stable identifier for your end-user for abuse detection and improved caching.</summary>
        [JsonPropertyName("user")]
        public string? User { get; set; }

        /// <summary>Include usage accounting details in the response.</summary>
        [JsonPropertyName("usage")]
        public UsageOption? Usage { get; set; }

        /// <summary>Options for built-in web search (non-plugin mode).</summary>
        [JsonPropertyName("web_search_options")]
        public WebSearchOptions? WebSearchOptions { get; set; }

        /// <summary>Plugins to apply to this request (e.g., web, file-parser).</summary>
        [JsonPropertyName("plugins")]
        public PluginWrapper[]? Plugins { get; set; }

        /// <summary>Enable or disable parallel tool calls.</summary>
        [JsonPropertyName("parallel_tool_calls")]
        public bool? ParallelToolCalls { get; set; }

        /// <summary>Controls verbosity of responses ("low"|"medium"|"high").</summary>
        [JsonPropertyName("verbosity")]
        public string? Verbosity { get; set; }

        /// <summary>Optional preset name to merge with this request.</summary>
        [JsonPropertyName("preset")]
        public string? Preset { get; set; }
    }
}
