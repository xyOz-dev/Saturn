using System.Text.Json.Serialization;

namespace Saturn.OpenRouter.Models.Api.Common
{
    /// <summary>
    /// Model routing parameters to try multiple models with fallback behavior.
    /// </summary>
    public sealed class ModelRouting
    {
        /// <summary>Candidate models to try in order.</summary>
        [JsonPropertyName("models")]
        public string[]? Models { get; set; }

        /// <summary>Routing strategy hint, e.g. "fallback".</summary>
        [JsonPropertyName("route")]
        public string? Route { get; set; }
    }
}
