using System.Text.Json.Serialization;

namespace Saturn.OpenRouter.Models.Api.Common
{
    public sealed class ModelRouting
    {
        [JsonPropertyName("models")]
        public string[]? Models { get; set; }

        [JsonPropertyName("route")]
        public string? Route { get; set; }
    }
}
