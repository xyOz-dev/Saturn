using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Saturn.Providers;

namespace Saturn.Tools.Search.Providers
{
    public sealed class SerpApiSearchProvider : SearchProviderBase
    {
        public override string Name => "serpapi";
        public override string DisplayName => "SerpAPI";
        public override string EnvironmentVariable => "SERPAPI_API_KEY";

        public override async Task<SearchResponse> SearchAsync(
            string query, int maxResults, ProviderSettings settings, HttpClient http, CancellationToken cancellationToken = default)
        {
            var apiKey = ResolveApiKey(settings);

            var url = $"https://serpapi.com/search.json?engine=google&q={Uri.EscapeDataString(query)}&num={maxResults}&api_key={Uri.EscapeDataString(apiKey)}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);

            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await ReadOrThrowAsync(response, cancellationToken).ConfigureAwait(false);

            var results = new List<SearchResult>();
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("organic_results", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in items.EnumerateArray())
                {
                    var resultUrl = item.TryGetProperty("link", out var l) ? l.GetString() : null;
                    if (string.IsNullOrEmpty(resultUrl)) continue;

                    var title = item.TryGetProperty("title", out var t) ? t.GetString() : null;
                    var snippet = item.TryGetProperty("snippet", out var s) ? s.GetString() : null;

                    results.Add(new SearchResult(title ?? resultUrl, resultUrl, CleanSnippet(snippet)));
                }
            }

            return new SearchResponse(results, DisplayName);
        }
    }
}
