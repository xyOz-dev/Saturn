using System.Text.Json.Serialization;

namespace Saturn.OpenRouter.Models.Api.Models
{
    /// <summary>
    /// Response envelope for the Models API endpoints.
    /// </summary>
    public sealed class ModelListResponse
    {
        /// <summary>The list of models returned by the API.</summary>
        [JsonPropertyName("data")]
        public Model[]? Data { get; set; }
    }
}