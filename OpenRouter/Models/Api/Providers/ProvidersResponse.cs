using System.Text.Json;

namespace Saturn.OpenRouter.Models.Api.Providers
{
    /// <summary>
    /// Typed-light container for the GET /providers endpoint.
    /// Because OpenRouter does not guarantee a stable schema for this route, this DTO exposes:
    /// - Root: the entire JSON response body as a JsonElement
    /// - Data: the top-level "data" property if present; otherwise null
    ///
    /// Consumers who need the exact, full payload shape should use the raw accessor that returns a JsonDocument
    /// and manually traverse the JSON.
    /// </summary>
    public sealed class ProvidersResponse
    {
        /// <summary>
        /// The entire JSON response body as a JsonElement. This allows access to any fields OpenRouter may add.
        /// </summary>
        public JsonElement Root { get; set; }

        /// <summary>
        /// The optional top-level "data" property if present; otherwise null.
        /// </summary>
        public JsonElement? Data { get; set; }
    }
}