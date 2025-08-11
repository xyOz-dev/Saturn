using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Saturn.OpenRouter.Http;
using Saturn.OpenRouter.Models.Api.Common;
using Saturn.OpenRouter.Models.Api.Generation;
using Saturn.OpenRouter.Serialization;

namespace Saturn.OpenRouter.Services
{
    /// <summary>
    /// Generation API service for GET /generation?id={id}.
    /// Exposes both a typed-light wrapper and a raw JSON accessor to remain robust
    /// against schema changes in the OpenRouter endpoint.
    /// </summary>
    public sealed class GenerationService
    {
        private readonly HttpClientAdapter _http;

        /// <summary>
        /// Internal constructor. Instances are exposed via <c>OpenRouterClient</c>.
        /// </summary>
        internal GenerationService(HttpClientAdapter http)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
        }

        /// <summary>
        /// GET /generation?id={id}
        /// Retrieves a specific generation's metadata and usage details.
        /// Returns a typed-light container with:
        /// - Root: the entire JSON response body as JsonElement
        /// - Best-effort fields mapped from top-level properties (Id, Model, Provider, Created)
        /// - Usage: parsed from a top-level "usage" object when present
        /// </summary>
        /// <param name="id">The generation identifier returned by a prior request (e.g., "gen-...").</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async Task<GenerationResponse> GetAsync(string id, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            var q = Uri.EscapeDataString(id);
            var path = $"generation?id={q}";

            var root = await _http.SendJsonAsync<JsonElement>(HttpMethod.Get, path, null, null, cancellationToken).ConfigureAwait(false);

            string? respId = null;
            string? model = null;
            string? provider = null;
            long? created = null;
            ResponseUsage? usage = null;

            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
                    respId = idEl.GetString();

                if (root.TryGetProperty("model", out var modelEl) && modelEl.ValueKind == JsonValueKind.String)
                    model = modelEl.GetString();

                if (root.TryGetProperty("provider", out var providerEl) && providerEl.ValueKind == JsonValueKind.String)
                    provider = providerEl.GetString();

                if (root.TryGetProperty("created", out var createdEl))
                {
                    if (createdEl.ValueKind == JsonValueKind.Number)
                    {
                        if (createdEl.TryGetInt64(out var c64)) created = c64;
                        else if (createdEl.TryGetInt32(out var c32)) created = c32;
                    }
                    else if (createdEl.ValueKind == JsonValueKind.String)
                    {
                        var s = createdEl.GetString();
                        if (!string.IsNullOrEmpty(s) && long.TryParse(s, out var c))
                            created = c;
                    }
                }

                if (root.TryGetProperty("usage", out var usageEl) && usageEl.ValueKind == JsonValueKind.Object)
                {
                    try
                    {
                        var options = Json.CreateDefaultOptions();
                        usage = System.Text.Json.JsonSerializer.Deserialize<ResponseUsage>(usageEl.GetRawText(), options);
                    }
                    catch
                    {
                        // Ignore mapping errors; Usage remains null for forward-compat.
                    }
                }
            }

            return new GenerationResponse
            {
                Id = respId,
                Model = model,
                Provider = provider,
                Created = created,
                Usage = usage,
                Root = root
            };
        }

        /// <summary>
        /// GET /generation?id={id}
        /// Returns the full response body as a JsonDocument for consumers who need complete control over the payload.
        /// </summary>
        /// <param name="id">The generation identifier returned by a prior request (e.g., "gen-...").</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async Task<JsonDocument> GetRawAsync(string id, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            var q = Uri.EscapeDataString(id);
            var path = $"generation?id={q}";

            using var response = await _http.SendRawAsync(HttpMethod.Get, path, null, null, cancellationToken).ConfigureAwait(false);

            var text = response.Content is null
                ? null
                : await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(text))
            {
                return JsonDocument.Parse("null");
            }

            return JsonDocument.Parse(text);
        }
    }
}