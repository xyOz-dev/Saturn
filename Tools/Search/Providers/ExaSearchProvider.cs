using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Saturn.Providers;

namespace Saturn.Tools.Search.Providers
{
    public sealed class ExaSearchProvider : SearchProviderBase
    {
        public override string Name => "exa";
        public override string DisplayName => "Exa";
        public override string EnvironmentVariable => "EXA_API_KEY";

        public override async Task<SearchResponse> SearchAsync(
            string query, int maxResults, ProviderSettings settings, HttpClient http, CancellationToken cancellationToken = default)
        {
            var apiKey = ResolveApiKey(settings);

            var payload = new Dictionary<string, object>
            {
                ["query"] = query,
                ["numResults"] = maxResults,
                ["contents"] = new Dictionary<string, object> { ["text"] = true }
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.exa.ai/search")
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
            request.Headers.TryAddWithoutValidation("x-api-key", apiKey);

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
                    var text = item.TryGetProperty("text", out var x) ? x.GetString() : null;

                    results.Add(new SearchResult(title ?? url, url, Truncate(CleanSnippet(text), 500)));
                }
            }

            return new SearchResponse(results, DisplayName);
        }
    }
}
