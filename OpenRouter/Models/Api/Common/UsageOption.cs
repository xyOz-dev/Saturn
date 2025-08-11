using System.Text.Json.Serialization;

namespace Saturn.OpenRouter.Models.Api.Common
{
    /// <summary>
    /// Controls inclusion of usage accounting in responses.
    /// </summary>
    public sealed class UsageOption
    {
        /// <summary>When true, include usage accounting details in responses.</summary>
        [JsonPropertyName("include")]
        public bool? Include { get; set; }
    }
}
