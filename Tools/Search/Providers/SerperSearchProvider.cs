using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Saturn.Providers;

namespace Saturn.Tools.Search.Providers
{
    public sealed class SerperSearchProvider : SearchProviderBase
    {
        public override string Name => "serper";
        public override string DisplayName => "Serper";
        public override string EnvironmentVariable => "SERPER_API_KEY";

        public override async Task<SearchResponse> SearchAsync(
            string query, int maxResults, ProviderSettings settings, HttpClient http, CancellationToken cancellationToken = default)
        {
            var apiKey = ResolveApiKey(settings);

            var payload = new Dictionary<string, object>
            {
                ["q"] = query,
                ["num"] = maxResults
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, "https://google.serper.dev/search")
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
            request.Headers.TryAddWithoutValidation("X-API-KEY", apiKey);

            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await ReadOrThrowAsync(response, cancellationToken).ConfigureAwait(false);

            var results = new List<SearchResult>();
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("organic", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in items.EnumerateArray())
                {
                    var url = item.TryGetProperty("link", out var l) ? l.GetString() : null;
                    if (string.IsNullOrEmpty(url)) continue;

                    var title = item.TryGetProperty("title", out var t) ? t.GetString() : null;
                    var snippet = item.TryGetProperty("snippet", out var s) ? s.GetString() : null;

                    results.Add(new SearchResult(title ?? url, url, CleanSnippet(snippet)));
                }
            }

            return new SearchResponse(results, DisplayName);
        }
    }
}
