using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Saturn.OpenRouter.Http;
using Saturn.OpenRouter.Models.Api.Chat;

namespace Saturn.OpenRouter.Services
{
    public sealed class ChatCompletionsService
    {
        private readonly HttpClientAdapter _http;

        internal ChatCompletionsService(HttpClientAdapter http)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
        }

        public async Task<ChatCompletionResponse?> CreateAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default)
        {
            if (request is null) throw new ArgumentNullException(nameof(request));
            if (request.Stream == true)
                throw new ArgumentException("Streaming is not supported by this method. Set Stream to null or false.", nameof(request));

            var response = await _http.SendJsonAsync<ChatCompletionResponse>(HttpMethod.Post, "chat/completions", request, null, cancellationToken).ConfigureAwait(false);

            if (response?.Error != null && (response.Error.Code != null || !string.IsNullOrWhiteSpace(response.Error.Message)))
            {
                var status = response.Error.Code is >= 400 and <= 599
                    ? (System.Net.HttpStatusCode)response.Error.Code.Value
                    : System.Net.HttpStatusCode.BadGateway;

                throw Errors.OpenRouterException.FromErrorBody(status, response.Error);
            }

            return response;
        }
    }
}
