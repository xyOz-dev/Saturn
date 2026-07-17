using System;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Saturn.OpenRouter.Http;
using Saturn.OpenRouter.Models.Api.Credits;

namespace Saturn.OpenRouter.Services
{
    public sealed class CreditsService
    {
        private readonly HttpClientAdapter _http;

        internal CreditsService(HttpClientAdapter http)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
        }

        public async Task<CreditsResponse> GetAsync(CancellationToken cancellationToken = default)
        {
            var root = await _http
                .SendJsonAsync<JsonElement>(HttpMethod.Get, "credits", null, null, cancellationToken)
                .ConfigureAwait(false);

            JsonElement? data = null;
            decimal? totalCredits = null;
            decimal? totalUsage = null;

            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var d))
            {
                data = d;

                if (d.ValueKind == JsonValueKind.Object)
                {
                    if (d.TryGetProperty("total_credits", out var tc))
                    {
                        totalCredits = ParseBestEffortDecimal(tc);
                    }

                    if (d.TryGetProperty("total_usage", out var tu))
                    {
                        totalUsage = ParseBestEffortDecimal(tu);
                    }
                }
            }

            return new CreditsResponse
            {
                Root = root,
                Data = data,
                TotalCredits = totalCredits,
                TotalUsage = totalUsage
            };
        }

        public async Task<JsonDocument> GetRawAsync(CancellationToken cancellationToken = default)
        {
            using var response = await _http
                .SendRawAsync(HttpMethod.Get, "credits", null, null, cancellationToken)
                .ConfigureAwait(false);

            var text = response.Content is null
                ? null
                : await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(text))
            {
                return JsonDocument.Parse("null");
            }

            return JsonDocument.Parse(text);
        }

        private static decimal? ParseBestEffortDecimal(JsonElement el)
        {
            try
            {
                if (el.ValueKind == JsonValueKind.Number)
                {
                    if (el.TryGetDecimal(out var d))
                        return d;

                    if (el.TryGetDouble(out var dbl))
                        return (decimal)dbl;
                }
                else if (el.ValueKind == JsonValueKind.String)
                {
                    var s = el.GetString();
                    if (!string.IsNullOrWhiteSpace(s) && decimal.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                        return parsed;
                }
            }
            catch
            {
            }

            return null;
        }
    }
}
