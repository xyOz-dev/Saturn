using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Saturn.OpenRouter.Http;
using Saturn.OpenRouter.Models.Api.Completions;

namespace Saturn.OpenRouter.Services
{
    /// <summary>
    /// Text Completions API service (non-streaming).
    /// Encapsulates POST /completions and returns typed non-streaming responses.
    /// </summary>
    public sealed class CompletionsService
    {
        private readonly HttpClientAdapter _http;

        /// <summary>
        /// Internal constructor. Instances are exposed via <c>OpenRouterClient</c>.
        /// </summary>
        /// <param name="http">HTTP client adapter with configured base URL, authorization, and headers.</param>
        internal CompletionsService(HttpClientAdapter http)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
        }

        /// <summary>
        /// POST /completions (non-streaming).
        /// Sends the specified request and returns the typed non-streaming response.
        /// </summary>
        /// <param name="request">A fully populated text completion request DTO.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The non-streaming text completion response DTO.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is null.</exception>
        /// <exception cref="Saturn.OpenRouter.Errors.OpenRouterException">Thrown for non-success HTTP responses by the adapter after parsing API errors.</exception>
        public Task<TextCompletionResponse?> CreateAsync(TextCompletionRequest request, CancellationToken cancellationToken = default)
        {
            if (request is null) throw new ArgumentNullException(nameof(request));

            // Relative path against adapter.BaseAddress (".../api/v1/")
            return _http.SendJsonAsync<TextCompletionResponse>(HttpMethod.Post, "completions", request, null, cancellationToken);
        }
    }
}