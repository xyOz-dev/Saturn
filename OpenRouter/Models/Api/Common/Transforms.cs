using System.Text.Json.Serialization;

namespace Saturn.OpenRouter.Models.Api.Common
{
    /// <summary>
    /// Message transforms to apply before sending to providers (e.g., "middle-out").
    /// </summary>
    public sealed class Transforms
    {
        /// <summary>Transforms to apply (e.g., ["middle-out"]).</summary>
        [JsonPropertyName("transforms")]
        public string[]? Items { get; set; }
    }
}
