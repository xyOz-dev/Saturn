using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Saturn.Providers;

namespace Saturn.Tools.Search.Providers
{
    public sealed class TavilySearchProvider : SearchProviderBase
    {
        public override string Name => "tavily";
        public override string DisplayName => "Tavily";
        public override string EnvironmentVariable => "TAVILY_API_KEY";

        public override async Task<SearchResponse> SearchAsync(
            string query, int maxResults, ProviderSettings settings, HttpClient http, CancellationToken cancellationToken = default)
        {
            var apiKey = ResolveApiKey(settings);

            var payload = new Dictionary<string, object>
            {
                ["query"] = query,
                ["max_results"] = maxResults
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.tavily.com/search")
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await ReadOrThrowAsync(response, cancellationToken).ConfigureAwait(false);

            var results = new List<SearchResult>();
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("results", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in items.EnumerateArray())
                {
                    var url = item.TryGetProperty("url", out var u) ? u.GetString() : null;
                    if (string.IsNullOrEmpty(url)) continue;

                    var title = item.TryGetProperty("title", out var t) ? t.GetString() : null;
                    var content = item.TryGetProperty("content", out var c) ? c.GetString() : null;

                    results.Add(new SearchResult(title ?? url, url, CleanSnippet(content)));
                }
            }

            return new SearchResponse(results, DisplayName);
        }
    }
}
