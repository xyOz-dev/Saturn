using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Saturn.OpenRouter.Http;
using Saturn.OpenRouter.Models.Api.Models;

namespace Saturn.OpenRouter.Services
{
    /// <summary>
    /// Models API service.
    /// Provides methods to fetch model listings and endpoints.
    /// </summary>
    public sealed class ModelsService
    {
        private readonly HttpClientAdapter _http;

        /// <summary>
        /// Internal constructor. Instances are exposed via <c>OpenRouterClient</c>.
        /// </summary>
        internal ModelsService(HttpClientAdapter http)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
        }

        /// <summary>
        /// GET /models
        /// Fetch a list of all available models.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        public Task<ModelListResponse?> ListAllAsync(CancellationToken cancellationToken = default)
            => _http.SendJsonAsync<ModelListResponse>(HttpMethod.Get, "models", null, null, cancellationToken);

        /// <summary>
        /// GET /models/user
        /// Fetch a list of models tailored to the user's preferences. Requires API key.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        public Task<ModelListResponse?> ListByUserPreferencesAsync(CancellationToken cancellationToken = default)
            => _http.SendJsonAsync<ModelListResponse>(HttpMethod.Get, "models/user", null, null, cancellationToken);

        /// <summary>
        /// GET /models/{author}/{slug}/endpoints
        /// Fetch provider endpoints for a specific model.
        /// </summary>
        /// <param name="author">Model author (e.g., "openai").</param>
        /// <param name="slug">Model slug (e.g., "gpt-4o").</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public Task<ModelListResponse?> ListEndpointsAsync(string author, string slug, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(author)) throw new ArgumentNullException(nameof(author));
            if (string.IsNullOrWhiteSpace(slug)) throw new ArgumentNullException(nameof(slug));

            var a = Uri.EscapeDataString(author);
            var s = Uri.EscapeDataString(slug);
            var path = $"models/{a}/{s}/endpoints";

            return _http.SendJsonAsync<ModelListResponse>(HttpMethod.Get, path, null, null, cancellationToken);
        }
    }
}