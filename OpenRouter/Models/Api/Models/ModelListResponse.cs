using System.Text.Json.Serialization;

namespace Saturn.OpenRouter.Models.Api.Models
{
    public sealed class ModelListResponse
    {
        [JsonPropertyName("data")]
        public Model[]? Data { get; set; }
    }
}