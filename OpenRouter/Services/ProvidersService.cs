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
    public sealed class ProvidersService
    {
        private readonly HttpClientAdapter _http;

        internal ProvidersService(HttpClientAdapter http)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
        }

        public async Task<ProvidersResponse> ListAsync(CancellationToken cancellationToken = default)
        {
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

        public async Task<JsonDocument> ListRawAsync(CancellationToken cancellationToken = default)
        {
            using var response = await _http.SendRawAsync(HttpMethod.Get, "providers", null, null, cancellationToken).ConfigureAwait(false);

            var text = response.Content is null
                ? null
                : await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(text))
            {
                return JsonDocument.Parse("null");
            }

            return JsonDocument.Parse(text);
        }
    }
}
