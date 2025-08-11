using System.Text.Json.Serialization;

namespace Saturn.OpenRouter.Models.Api.Chat
{
    /// <summary>
    /// Cache control breakpoint marker (currently "ephemeral" only).
    /// </summary>
    public sealed class CacheControl
    {
        /// <summary>Type discriminator for caching behavior. "ephemeral" indicates temporary caching.</summary>
        [JsonPropertyName("type")]
        public string Type { get; set; } = "ephemeral";
    }
}
