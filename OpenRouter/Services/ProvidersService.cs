using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Saturn.OpenRouter.Http;
using Saturn.OpenRouter.Models.Api.Providers;
using Saturn.OpenRouter.Serialization;

namespace Saturn.OpenRouter.Services
{
    /// <summary>
    /// Providers API service for GET /providers.
    /// Exposes both a typed-light wrapper and a raw JSON accessor to remain robust
    /// against schema changes in the OpenRouter endpoint.
    /// </summary>
    public sealed class ProvidersService
    {
        private readonly HttpClientAdapter _http;

        /// <summary>
        /// Internal constructor. Instances are exposed via <c>OpenRouterClient</c>.
        /// </summary>
        internal ProvidersService(HttpClientAdapter http)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
        }

        /// <summary>
        /// GET /providers
        /// Returns a typed-light container with:
        /// - Root: the entire JSON response body as JsonElement
        /// - Data: the top-level "data" property if present; otherwise null
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async Task<ProvidersResponse> ListAsync(CancellationToken cancellationToken = default)
        {
            // Use central JSON options via the adapter's JSON pipeline.
            // Important: path is relative (no leading slash) to respect BaseAddress (e.g., .../api/v1/).
            var root = await _http.SendJsonAsync<JsonElement>(HttpMethod.Get, "providers", null, null, cancellationToken).ConfigureAwait(false);

            JsonElement? data = null;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var d))
            {
                data = d;
            }

            return new ProvidersResponse
            {
                Root = root,
                Data = data
            };
        }

        /// <summary>
        /// GET /providers
        /// Returns a raw JsonDocument for consumers who need full control over the payload shape.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async Task<JsonDocument> ListRawAsync(CancellationToken cancellationToken = default)
        {
            using var response = await _http.SendRawAsync(HttpMethod.Get, "providers", null, null, cancellationToken).ConfigureAwait(false);

            // Read the full content as string to handle potential empty bodies gracefully.
            var text = response.Content is null
                ? null
                : await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(text))
            {
                // Provide a valid JSON document even if the body is empty.
                return JsonDocument.Parse("null");
            }

            return JsonDocument.Parse(text);
        }
    }
}