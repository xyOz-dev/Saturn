using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Saturn.OpenRouter.Http;
using Saturn.OpenRouter.Models.Api.Models;

namespace Saturn.OpenRouter.Services
{
    public sealed class ModelsService
    {
        private readonly HttpClientAdapter _http;

        internal ModelsService(HttpClientAdapter http)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
        }

        public Task<ModelListResponse?> ListAllAsync(CancellationToken cancellationToken = default)
            => _http.SendJsonAsync<ModelListResponse>(HttpMethod.Get, "models", null, null, cancellationToken);

        public Task<ModelListResponse?> ListByUserPreferencesAsync(CancellationToken cancellationToken = default)
            => _http.SendJsonAsync<ModelListResponse>(HttpMethod.Get, "models/user", null, null, cancellationToken);

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
