using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Saturn.OpenRouter.Http;
using Saturn.OpenRouter.Models.Api.Completions;

namespace Saturn.OpenRouter.Services
{
    public sealed class CompletionsService
    {
        private readonly HttpClientAdapter _http;

        internal CompletionsService(HttpClientAdapter http)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
        }

        public Task<TextCompletionResponse?> CreateAsync(TextCompletionRequest request, CancellationToken cancellationToken = default)
        {
            if (request is null) throw new ArgumentNullException(nameof(request));

            return _http.SendJsonAsync<TextCompletionResponse>(HttpMethod.Post, "completions", request, null, cancellationToken);
        }
    }
}
