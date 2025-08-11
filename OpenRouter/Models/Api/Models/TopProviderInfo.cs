using System.Text.Json.Serialization;

namespace Saturn.OpenRouter.Models.Api.Models
{
    /// <summary>
    /// Information about the top provider for a model, including context limits and moderation status.
    /// </summary>
    public sealed class TopProviderInfo
    {
        /// <summary>Maximum supported context length (in tokens) for this provider.</summary>
        [JsonPropertyName("context_length")]
        public int? ContextLength { get; set; }

        /// <summary>Maximum completion tokens allowed for this provider.</summary>
        [JsonPropertyName("max_completion_tokens")]
        public int? MaxCompletionTokens { get; set; }

        /// <summary>True if the provider is moderated.</summary>
        [JsonPropertyName("is_moderated")]
        public bool? IsModerated { get; set; }
    }
}