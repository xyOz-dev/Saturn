using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using Saturn.Tests.Providers;
using Saturn.Tools;
using Xunit;

namespace Saturn.Tests.Tools
{
    [Collection("Configuration")]
    public class WebSearchToolTests : IDisposable
    {
        private static readonly string[] ProviderEnvVars =
            { "TAVILY_API_KEY", "BRAVE_API_KEY", "SERPER_API_KEY", "SERPAPI_API_KEY", "EXA_API_KEY" };

        private readonly string _configDir;
        private readonly Dictionary<string, string?> _savedEnv = new();

        public WebSearchToolTests()
        {
            _configDir = Path.Combine(Path.GetTempPath(), "SaturnWebSearchTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_configDir);
            Environment.SetEnvironmentVariable("SATURN_CONFIG_DIR", _configDir);

            // Snapshot and clear provider keys so tests are deterministic regardless of host env.
            foreach (var name in ProviderEnvVars)
            {
                _savedEnv[name] = Environment.GetEnvironmentVariable(name);
                Environment.SetEnvironmentVariable(name, null);
            }
        }

        public void Dispose()
        {
            WebSearchTool.HttpClientOverride = null;
            Environment.SetEnvironmentVariable("SATURN_CONFIG_DIR", null);
            foreach (var kvp in _savedEnv)
            {
                Environment.SetEnvironmentVariable(kvp.Key, kvp.Value);
            }
            try { Directory.Delete(_configDir, recursive: true); } catch { }
        }

        private static void StubHttp(string body, HttpStatusCode status = HttpStatusCode.OK)
        {
            var log = new List<(HttpRequestMessage, string)>();
            WebSearchTool.HttpClientOverride = new HttpClient(
                new StubHttpHandler(_ => StubHttpHandler.Json(body, status), log));
        }

        [Fact]
        public async Task NoProviderConfigured_ReturnsActionableError()
        {
            var tool = new WebSearchTool();

            var result = await tool.ExecuteAsync(new Dictionary<string, object> { ["query"] = "anything" });

            result.Success.Should().BeFalse();
            result.Error.Should().Contain("TAVILY_API_KEY");
            result.Error.Should().Contain("Search Provider");
        }

        [Fact]
        public async Task EmptyQuery_ReturnsError()
        {
            var tool = new WebSearchTool();

            var result = await tool.ExecuteAsync(new Dictionary<string, object> { ["query"] = "  " });

            result.Success.Should().BeFalse();
            result.Error.Should().Contain("empty");
        }

        [Fact]
        public async Task ProviderOverrideWithEnvKey_ReturnsFormattedResults()
        {
            Environment.SetEnvironmentVariable("TAVILY_API_KEY", "sk-env");
            StubHttp("""{"results":[{"title":"Hit","url":"https://x.com","content":"snippet text"}]}""");
            var tool = new WebSearchTool();

            var result = await tool.ExecuteAsync(new Dictionary<string, object>
            {
                ["query"] = "saturn",
                ["provider"] = "tavily"
            });

            result.Success.Should().BeTrue();
            result.FormattedOutput.Should().Contain("Web search via Tavily");
            result.FormattedOutput.Should().Contain("1. Hit");
            result.FormattedOutput.Should().Contain("https://x.com");
            result.FormattedOutput.Should().Contain("snippet text");
        }

        [Fact]
        public async Task UnknownProviderOverride_ReturnsError()
        {
            var tool = new WebSearchTool();

            var result = await tool.ExecuteAsync(new Dictionary<string, object>
            {
                ["query"] = "q",
                ["provider"] = "nope"
            });

            result.Success.Should().BeFalse();
            result.Error.Should().Contain("Unknown search provider");
        }

        [Fact]
        public async Task ZeroResults_ReportsNoResults()
        {
            Environment.SetEnvironmentVariable("BRAVE_API_KEY", "sk-env");
            StubHttp("""{"web":{"results":[]}}""");
            var tool = new WebSearchTool();

            var result = await tool.ExecuteAsync(new Dictionary<string, object>
            {
                ["query"] = "obscure",
                ["provider"] = "brave"
            });

            result.Success.Should().BeTrue();
            result.FormattedOutput.Should().Contain("no results");
        }
    }
}
