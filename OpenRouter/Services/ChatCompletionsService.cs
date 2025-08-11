using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Saturn.OpenRouter.Http;
using Saturn.OpenRouter.Models.Api.Chat;

namespace Saturn.OpenRouter.Services
{
    /// <summary>
    /// Chat Completions API service (non-streaming).
    /// Encapsulates POST /chat/completions and returns typed non-streaming responses.
    /// </summary>
    public sealed class ChatCompletionsService
    {
        private readonly HttpClientAdapter _http;

        /// <summary>
        /// Internal constructor. Instances are exposed via <c>OpenRouterClient</c>.
        /// </summary>
        /// <param name="http">HTTP client adapter with configured base URL, authorization, and headers.</param>
        internal ChatCompletionsService(HttpClientAdapter http)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
        }

        /// <summary>
        /// POST /chat/completions (non-streaming).
        /// Sends the specified request and returns the typed non-streaming response.
        /// </summary>
        /// <param name="request">A fully populated chat completion request DTO. Must not set <c>Stream</c> = true in this non-streaming method.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The non-streaming chat completion response DTO.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="request"/>.Stream is true (streaming is not supported here).</exception>
        /// <exception cref="Saturn.OpenRouter.Errors.OpenRouterException">Thrown for non-success HTTP responses by the adapter after parsing API errors.</exception>
        public Task<ChatCompletionResponse?> CreateAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default)
        {
            if (request is null) throw new ArgumentNullException(nameof(request));
            if (request.Stream == true)
                throw new ArgumentException("Streaming is not supported by this method. Set Stream to null or false.", nameof(request));

            // Relative path against adapter.BaseAddress (".../api/v1/")
            return _http.SendJsonAsync<ChatCompletionResponse>(HttpMethod.Post, "chat/completions", request, null, cancellationToken);
        }
    }
}