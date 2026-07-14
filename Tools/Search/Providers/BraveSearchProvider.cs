using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Saturn.Providers;

namespace Saturn.Tools.Search.Providers
{
    public sealed class BraveSearchProvider : SearchProviderBase
    {
        public override string Name => "brave";
        public override string DisplayName => "Brave Search";
        public override string EnvironmentVariable => "BRAVE_API_KEY";

        public override async Task<SearchResponse> SearchAsync(
            string query, int maxResults, ProviderSettings settings, HttpClient http, CancellationToken cancellationToken = default)
        {
            var apiKey = ResolveApiKey(settings);

            var url = $"https://api.search.brave.com/res/v1/web/search?q={Uri.EscapeDataString(query)}&count={maxResults}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("X-Subscription-Token", apiKey);
            request.Headers.TryAddWithoutValidation("Accept", "application/json");

            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await ReadOrThrowAsync(response, cancellationToken).ConfigureAwait(false);

            var results = new List<SearchResult>();
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("web", out var web) &&
                web.TryGetProperty("results", out var items) &&
                items.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in items.EnumerateArray())
                {
                    var resultUrl = item.TryGetProperty("url", out var u) ? u.GetString() : null;
                    if (string.IsNullOrEmpty(resultUrl)) continue;

                    var title = item.TryGetProperty("title", out var t) ? t.GetString() : null;
                    var description = item.TryGetProperty("description", out var d) ? d.GetString() : null;

                    results.Add(new SearchResult(CleanSnippet(title), resultUrl, CleanSnippet(description)));
                }
            }

            return new SearchResponse(results, DisplayName);
        }
    }
}
