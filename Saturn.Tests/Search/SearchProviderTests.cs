using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using Saturn.Providers;
using Saturn.Tests.Providers;
using Saturn.Tools.Search;
using Saturn.Tools.Search.Providers;
using Xunit;

namespace Saturn.Tests.Search
{
    [Collection("Configuration")]
    public class SearchProviderTests : IDisposable
    {
        private static readonly string[] ProviderEnvVars =
            { "TAVILY_API_KEY", "BRAVE_API_KEY", "SERPER_API_KEY", "SERPAPI_API_KEY", "EXA_API_KEY" };

        private readonly Dictionary<string, string?> _savedEnv = new();

        public SearchProviderTests()
        {
            // ResolveApiKey falls back to these env vars, so clear them for determinism
            // regardless of the host environment; the collection serializes us against
            // other tests that set them.
            foreach (var name in ProviderEnvVars)
            {
                _savedEnv[name] = Environment.GetEnvironmentVariable(name);
                Environment.SetEnvironmentVariable(name, null);
            }
        }

        public void Dispose()
        {
            foreach (var kvp in _savedEnv)
            {
                Environment.SetEnvironmentVariable(kvp.Key, kvp.Value);
            }
        }

        private static (HttpClient http, List<(HttpRequestMessage Request, string Body)> log) Stub(
            string responseBody, HttpStatusCode status = HttpStatusCode.OK)
        {
            var log = new List<(HttpRequestMessage, string)>();
            var handler = new StubHttpHandler(_ => StubHttpHandler.Json(responseBody, status), log);
            return (new HttpClient(handler), log);
        }

        private static ProviderSettings Key(string value)
        {
            var s = new ProviderSettings();
            s.Set(SearchProviderBase.ApiKeySetting, value);
            return s;
        }

        [Fact]
        public async Task Tavily_BuildsPostWithBearer_ParsesResults()
        {
            var (http, log) = Stub("""{"results":[{"title":"T","url":"https://a.com","content":"body"}]}""");
            var provider = new TavilySearchProvider();

            var response = await provider.SearchAsync("hello world", 5, Key("sk-tav"), http);

            var (request, body) = log.Single();
            request.Method.Should().Be(HttpMethod.Post);
            request.RequestUri!.ToString().Should().Be("https://api.tavily.com/search");
            request.Headers.Authorization!.Scheme.Should().Be("Bearer");
            request.Headers.Authorization.Parameter.Should().Be("sk-tav");
            body.Should().Contain("\"query\":\"hello world\"").And.Contain("\"max_results\":5");

            response.Provider.Should().Be("Tavily");
            response.Results.Should().ContainSingle();
            response.Results[0].Should().Be(new SearchResult("T", "https://a.com", "body"));
        }

        [Fact]
        public async Task Brave_BuildsGetWithTokenHeader_StripsHtml()
        {
            var (http, log) = Stub("""{"web":{"results":[{"title":"Ti","url":"https://b.com","description":"a <strong>bold</strong> hit"}]}}""");
            var provider = new BraveSearchProvider();

            var response = await provider.SearchAsync("c# tips", 3, Key("brave-key"), http);

            var (request, _) = log.Single();
            request.Method.Should().Be(HttpMethod.Get);
            request.RequestUri!.AbsoluteUri.Should().StartWith("https://api.search.brave.com/res/v1/web/search?q=c%23");
            request.RequestUri!.AbsoluteUri.Should().Contain("count=3");
            request.Headers.GetValues("X-Subscription-Token").Single().Should().Be("brave-key");

            response.Results.Single().Snippet.Should().Be("a bold hit");
        }

        [Fact]
        public async Task Serper_BuildsPostWithApiKeyHeader_ParsesOrganic()
        {
            var (http, log) = Stub("""{"organic":[{"title":"S","link":"https://s.com","snippet":"snip"}]}""");
            var provider = new SerperSearchProvider();

            var response = await provider.SearchAsync("query", 4, Key("serper-key"), http);

            var (request, body) = log.Single();
            request.Method.Should().Be(HttpMethod.Post);
            request.RequestUri!.ToString().Should().Be("https://google.serper.dev/search");
            request.Headers.GetValues("X-API-KEY").Single().Should().Be("serper-key");
            body.Should().Contain("\"q\":\"query\"").And.Contain("\"num\":4");

            response.Results.Single().Should().Be(new SearchResult("S", "https://s.com", "snip"));
        }

        [Fact]
        public async Task SerpApi_BuildsGetWithApiKeyQueryParam()
        {
            var (http, log) = Stub("""{"organic_results":[{"title":"G","link":"https://g.com","snippet":"gs"}]}""");
            var provider = new SerpApiSearchProvider();

            var response = await provider.SearchAsync("a b", 2, Key("serp-key"), http);

            var (request, _) = log.Single();
            request.Method.Should().Be(HttpMethod.Get);
            request.RequestUri!.AbsoluteUri.Should().StartWith("https://serpapi.com/search.json?engine=google&q=a");
            request.RequestUri!.AbsoluteUri.Should().Contain("num=2").And.Contain("api_key=serp-key");

            response.Results.Single().Url.Should().Be("https://g.com");
        }

        [Fact]
        public async Task Exa_BuildsPostWithApiKeyHeader_TruncatesText()
        {
            var longText = new string('x', 800);
            var (http, log) = Stub($$"""{"results":[{"title":"E","url":"https://e.com","text":"{{longText}}"}]}""");
            var provider = new ExaSearchProvider();

            var response = await provider.SearchAsync("q", 6, Key("exa-key"), http);

            var (request, body) = log.Single();
            request.Method.Should().Be(HttpMethod.Post);
            request.RequestUri!.ToString().Should().Be("https://api.exa.ai/search");
            request.Headers.GetValues("x-api-key").Single().Should().Be("exa-key");
            body.Should().Contain("\"numResults\":6").And.Contain("\"contents\"");

            response.Results.Single().Snippet.Length.Should().BeLessThan(longText.Length);
            response.Results.Single().Snippet.Should().EndWith("…");
        }

        [Fact]
        public async Task MalformedEntriesAreSkipped()
        {
            // Second entry has no url and must be dropped without throwing.
            var (http, _) = Stub("""{"results":[{"title":"ok","url":"https://ok.com","content":"c"},{"title":"bad"}]}""");
            var provider = new TavilySearchProvider();

            var response = await provider.SearchAsync("q", 5, Key("k"), http);

            response.Results.Should().ContainSingle();
            response.Results[0].Url.Should().Be("https://ok.com");
        }

        [Fact]
        public async Task Unauthorized_ThrowsWithApiKeyMessage()
        {
            var (http, _) = Stub("""{"error":"nope"}""", HttpStatusCode.Unauthorized);
            var provider = new BraveSearchProvider();

            var act = () => provider.SearchAsync("q", 5, Key("bad"), http);

            (await act.Should().ThrowAsync<HttpRequestException>())
                .Which.Message.Should().Contain("API key");
        }

        [Fact]
        public async Task ServerError_ThrowsWithStatus()
        {
            var (http, _) = Stub("boom", HttpStatusCode.InternalServerError);
            var provider = new SerperSearchProvider();

            var act = () => provider.SearchAsync("q", 5, Key("k"), http);

            (await act.Should().ThrowAsync<HttpRequestException>())
                .Which.Message.Should().Contain("500");
        }

        [Fact]
        public async Task MissingApiKey_ThrowsInvalidOperation()
        {
            var (http, _) = Stub("{}");
            var provider = new TavilySearchProvider();

            var act = () => provider.SearchAsync("q", 5, new ProviderSettings(), http);

            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        [Fact]
        public void EveryProvider_ExposesSecretApiKeyDescriptor()
        {
            foreach (var provider in SearchProviderRegistry.All)
            {
                var descriptor = provider.SettingDescriptors.Should().ContainSingle().Subject;
                descriptor.Key.Should().Be("apiKey");
                descriptor.Kind.Should().Be(ProviderSettingKind.Secret);
                descriptor.EnvironmentVariable.Should().NotBeNullOrWhiteSpace();
            }
        }

        [Fact]
        public void Registry_ContainsAllFiveProviders()
        {
            SearchProviderRegistry.All.Select(p => p.Name).Should()
                .BeEquivalentTo(new[] { "tavily", "brave", "serper", "serpapi", "exa" });
        }
    }
}
