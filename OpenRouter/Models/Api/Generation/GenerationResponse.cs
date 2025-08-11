using System.Text.Json;
using Saturn.OpenRouter.Models.Api.Common;

namespace Saturn.OpenRouter.Models.Api.Generation
{
    /// <summary>
    /// Typed-light envelope for GET /generation responses.
    /// The schema of this endpoint may vary over time and across providers.
    /// This DTO exposes a small set of best-effort strongly-typed properties
    /// and always retains the entire JSON payload in <see cref="Root"/> for forward compatibility.
    ///
    /// Notes:
    /// - Id, Model, Provider, and Created will be populated when present at the top level.
    /// - Usage will be populated when a top-level "usage" object is present and can be mapped
    ///   to <see cref="Saturn.OpenRouter.Models.Api.Common.ResponseUsage"/>.
    /// - For complete control over the payload, use the raw JsonDocument accessors on the service.
    /// </summary>
    public sealed class GenerationResponse
    {
        /// <summary>
        /// Optional top-level "id" if present.
        /// </summary>
        public string? Id { get; set; }

        /// <summary>
        /// Optional top-level "model" slug if present.
        /// </summary>
        public string? Model { get; set; }

        /// <summary>
        /// Optional top-level "provider" name if present.
        /// </summary>
        public string? Provider { get; set; }

        /// <summary>
        /// Optional top-level "created" Unix timestamp (seconds) if present.
        /// </summary>
        public long? Created { get; set; }

        /// <summary>
        /// Optional parsed "usage" object, normalized into <see cref="ResponseUsage"/> when present.
        /// </summary>
        public ResponseUsage? Usage { get; set; }

        /// <summary>
        /// The entire raw JSON response for forward compatibility.
        /// </summary>
        public JsonElement Root { get; set; }
    }
}