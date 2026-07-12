using System.Text.Json.Serialization;

namespace Saturn.OpenRouter.Models.Api.Chat
{
    public sealed class CacheControl
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "ephemeral";
    }
}
