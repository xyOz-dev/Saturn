using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Saturn.Providers;

namespace Saturn.Tools.Search.Providers
{
    /// <summary>
    /// Shared helpers for the built-in search providers: the single API-key descriptor,
    /// error mapping, and snippet cleanup.
    /// </summary>
    public abstract class SearchProviderBase : ISearchProvider
    {
        public abstract string Name { get; }
        public abstract string DisplayName { get; }
        public abstract string EnvironmentVariable { get; }

        public IReadOnlyList<ProviderSettingDescriptor> SettingDescriptors => new[]
        {
            new ProviderSettingDescriptor
            {
                Key = ApiKeySetting,
                Label = "API Key",
                Kind = ProviderSettingKind.Secret,
                Required = true,
                EnvironmentVariable = EnvironmentVariable
            }
        };

        public const string ApiKeySetting = "apiKey";

        public abstract Task<SearchResponse> SearchAsync(
            string query,
            int maxResults,
            ProviderSettings settings,
            HttpClient http,
            System.Threading.CancellationToken cancellationToken = default);

        protected string ResolveApiKey(ProviderSettings settings)
        {
            var key = SettingDescriptors[0].Resolve(settings);
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new InvalidOperationException(
                    $"{DisplayName} requires an API key. Set the {EnvironmentVariable} environment variable or configure it in settings.");
            }
            return key;
        }

        protected async Task<string> ReadOrThrowAsync(HttpResponseMessage response, System.Threading.CancellationToken cancellationToken)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return body;
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
            {
                throw new HttpRequestException($"{DisplayName} rejected the request: invalid or missing API key (HTTP {(int)response.StatusCode}).");
            }

            var detail = body.Length > 300 ? body.Substring(0, 300) : body;
            throw new HttpRequestException($"{DisplayName} search failed: HTTP {(int)response.StatusCode} {response.ReasonPhrase}. {detail}");
        }

        private static readonly Regex TagRegex = new("<.*?>", RegexOptions.Compiled | RegexOptions.Singleline);

        protected static string CleanSnippet(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            var stripped = TagRegex.Replace(text, string.Empty);
            return WebUtility.HtmlDecode(stripped).Trim();
        }

        protected static string Truncate(string text, int max)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= max)
            {
                return text ?? string.Empty;
            }
            return text.Substring(0, max).TrimEnd() + "…";
        }
    }
}
