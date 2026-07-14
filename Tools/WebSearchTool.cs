using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Saturn.Configuration;
using Saturn.Providers;
using Saturn.Tools.Core;
using Saturn.Tools.Search;

namespace Saturn.Tools
{
    public class WebSearchTool : ToolBase
    {
        private const int DefaultMaxResults = 8;
        private const int MinResults = 1;
        private const int MaxResults = 20;

        public override string Name => "web_search";

        public override string Description => @"Searches the web and returns a ranked list of titles, URLs, and snippets.

When to use:
- Finding current information, news, or documentation not in your training data
- Discovering URLs to then read in full with web_fetch
- Researching libraries, APIs, error messages, or how-to guidance

How to use:
- Set 'query' to your search terms
- Optionally set 'max_results' (default 8) or 'provider' to override the configured search provider

Requires a configured search provider (Tavily, Brave, Serper, SerpAPI, or Exa). If none is configured the tool returns instructions for configuring one.";

        private static readonly HttpClient DefaultHttpClient = new(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        })
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        // Test seam: allows tests to inject a stubbed HttpClient.
        internal static HttpClient? HttpClientOverride { get; set; }

        private static HttpClient Http => HttpClientOverride ?? DefaultHttpClient;

        protected override Dictionary<string, object> GetParameterProperties()
        {
            return new Dictionary<string, object>
            {
                { "query", new Dictionary<string, object>
                    {
                        { "type", "string" },
                        { "description", "The search query" }
                    }
                },
                { "max_results", new Dictionary<string, object>
                    {
                        { "type", "integer" },
                        { "default", DefaultMaxResults },
                        { "description", $"Maximum number of results to return (default {DefaultMaxResults}, {MinResults}-{MaxResults})" }
                    }
                },
                { "provider", new Dictionary<string, object>
                    {
                        { "type", "string" },
                        { "enum", SearchProviderRegistry.All.Select(p => p.Name).ToArray() },
                        { "description", "Override the configured search provider for this one search" }
                    }
                }
            };
        }

        protected override string[] GetRequiredParameters() => new[] { "query" };

        public override string GetDisplaySummary(Dictionary<string, object> parameters)
        {
            var query = GetParameter<string>(parameters, "query", "");
            return string.IsNullOrEmpty(query) ? "Searching the web" : $"Searching web: {TruncateString(query, 50)}";
        }

        public override async Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters)
        {
            var query = GetParameter<string>(parameters, "query");
            if (string.IsNullOrWhiteSpace(query))
            {
                return CreateErrorResult("Query cannot be empty");
            }

            var maxResults = Math.Clamp(GetParameter<int>(parameters, "max_results", DefaultMaxResults), MinResults, MaxResults);
            var providerOverride = GetParameter<string?>(parameters, "provider", null);

            var persisted = await ConfigurationManager.LoadConfigurationAsync();

            ISearchProvider? provider = null;
            if (!string.IsNullOrWhiteSpace(providerOverride))
            {
                if (!SearchProviderRegistry.TryGet(providerOverride, out provider))
                {
                    var known = string.Join(", ", SearchProviderRegistry.All.Select(p => p.Name));
                    return CreateErrorResult($"Unknown search provider '{providerOverride}'. Available: {known}.");
                }
            }
            else if (!string.IsNullOrWhiteSpace(persisted?.SearchProvider))
            {
                SearchProviderRegistry.TryGet(persisted!.SearchProvider!, out provider);
            }

            // Fall back to the first provider that already has a resolvable API key
            // (e.g. via environment variable), so env-only setups work with zero config.
            provider ??= SearchProviderRegistry.All.FirstOrDefault(p => HasApiKey(p, persisted));

            if (provider == null)
            {
                return CreateErrorResult(NoProviderMessage());
            }

            var settings = ConfigurationManager.GetSearchProviderSettings(persisted, provider.Name);
            if (!HasApiKey(provider, persisted))
            {
                return CreateErrorResult($"Search provider '{provider.DisplayName}' has no API key. " + NoProviderMessage());
            }

            try
            {
                var response = await provider.SearchAsync(query, maxResults, settings, Http);
                return CreateSuccessResult(response, FormatResults(query, response));
            }
            catch (TaskCanceledException)
            {
                return CreateErrorResult($"Web search timed out after 30 seconds ({provider.DisplayName}).");
            }
            catch (HttpRequestException ex)
            {
                return CreateErrorResult(ex.Message);
            }
            catch (Exception ex)
            {
                return CreateErrorResult($"Web search failed ({provider.DisplayName}): {ex.Message}");
            }
        }

        private static bool HasApiKey(ISearchProvider provider, Configuration.Objects.PersistedAgentConfiguration? persisted)
        {
            var settings = ConfigurationManager.GetSearchProviderSettings(persisted, provider.Name);
            var descriptor = provider.SettingDescriptors.FirstOrDefault(d => d.Kind == ProviderSettingKind.Secret);
            return descriptor != null && !string.IsNullOrWhiteSpace(descriptor.Resolve(settings));
        }

        private static string NoProviderMessage()
        {
            return "No web search provider is configured. Configure one in the TUI (Agent menu > Search Provider...), " +
                   "the web UI (Settings > Web Search), or set an environment variable: " +
                   "TAVILY_API_KEY, BRAVE_API_KEY, SERPER_API_KEY, SERPAPI_API_KEY, or EXA_API_KEY.";
        }

        private static string FormatResults(string query, SearchResponse response)
        {
            if (response.Results.Count == 0)
            {
                return $"Web search via {response.Provider}: \"{query}\" returned no results.";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Web search via {response.Provider}: \"{query}\" ({response.Results.Count} results)");
            sb.AppendLine();

            var index = 1;
            foreach (var result in response.Results)
            {
                sb.AppendLine($"{index}. {result.Title}");
                sb.AppendLine($"   {result.Url}");
                if (!string.IsNullOrEmpty(result.Snippet))
                {
                    sb.AppendLine($"   {result.Snippet}");
                }
                sb.AppendLine();
                index++;
            }

            return sb.ToString().TrimEnd();
        }
    }
}
