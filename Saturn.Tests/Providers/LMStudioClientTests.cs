using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Saturn.OpenRouter.Models.Api.Chat;
using Saturn.Providers;
using Xunit;

namespace Saturn.Tests.Providers
{
    public class LMStudioClientTests
    {
        private const string ChatResponseJson = """
            {"id":"1","object":"chat.completion","choices":[{"index":0,"message":{"role":"assistant","content":"hello"},"finish_reason":"stop"}]}
            """;

        private static (LMStudioClient Client, List<(HttpRequestMessage Request, string Body)> Log) CreateClient(
            Func<HttpRequestMessage, HttpResponseMessage> responder,
            string baseUrl = "http://localhost:1234/v1")
        {
            var log = new List<(HttpRequestMessage, string)>();
            var client = new LMStudioClient(baseUrl, TimeSpan.FromSeconds(30), () => new StubHttpHandler(responder, log));
            return (client, log);
        }

        private static ChatCompletionRequest SimpleRequest() => new()
        {
            Model = "test-model",
            Messages = new[]
            {
                new Message { Role = "user", Content = JsonDocument.Parse("\"hi\"").RootElement }
            }
        };

        [Theory]
        [InlineData("http://localhost:1234")]
        [InlineData("http://localhost:1234/")]
        [InlineData("http://localhost:1234/v1")]
        [InlineData("http://localhost:1234/v1/")]
        public void BaseUrl_IsNormalizedToV1(string input)
        {
            var (client, _) = CreateClient(_ => StubHttpHandler.Json("{}"), input);
            client.BaseUrl.Should().Be("http://localhost:1234/v1");
        }

        [Fact]
        public void Constructor_InvalidUrl_Throws()
        {
            var act = () => new LMStudioClient("not a url", TimeSpan.FromSeconds(1));
            act.Should().Throw<InvalidOperationException>();
        }

        [Fact]
        public async Task ChatAsync_SendsNoAuthHeaderAndOmitsNullExtensionFields()
        {
            var (client, log) = CreateClient(request =>
                request.RequestUri!.AbsolutePath.EndsWith("/chat/completions")
                    ? StubHttpHandler.Json(ChatResponseJson)
                    : StubHttpHandler.Json("{}"));

            var response = await client.ChatAsync(SimpleRequest());

            response!.Choices![0].Message!.Content.Should().Be("hello");

            var (request, body) = log.Single(e => e.Request.RequestUri!.AbsolutePath.EndsWith("/chat/completions"));
            request.RequestUri!.AbsolutePath.Should().Be("/v1/chat/completions");
            request.Headers.Authorization.Should().BeNull();

            using var json = JsonDocument.Parse(body);
            json.RootElement.TryGetProperty("transforms", out _).Should().BeFalse();
            json.RootElement.TryGetProperty("usage", out _).Should().BeFalse();
            json.RootElement.TryGetProperty("tool_choice", out _).Should().BeFalse();
        }

        [Fact]
        public async Task ChatAsync_ConnectionRefused_ThrowsActionableMessage()
        {
            var (client, _) = CreateClient(_ => throw new HttpRequestException("connection refused"));

            var act = () => client.ChatAsync(SimpleRequest());

            (await act.Should().ThrowAsync<InvalidOperationException>())
                .WithMessage("*Cannot reach LM Studio at http://localhost:1234/v1*");
        }

        [Fact]
        public async Task ListModelsAsync_MergesNativeApiEnrichment()
        {
            var (client, _) = CreateClient(request =>
            {
                var path = request.RequestUri!.AbsolutePath;
                if (path == "/v1/models")
                {
                    return StubHttpHandler.Json("""{"data":[{"id":"model-a","object":"model"},{"id":"model-b","object":"model"}]}""");
                }
                if (path == "/api/v0/models")
                {
                    return StubHttpHandler.Json("""{"data":[{"id":"model-b","max_context_length":8192,"state":"loaded"},{"id":"model-a","state":"not-loaded"}]}""");
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

            var models = await client.ListModelsAsync();

            models.Should().HaveCount(2);
            // Loaded models sort first.
            models[0].Id.Should().Be("model-b");
            models[0].IsLoaded.Should().BeTrue();
            models[0].ContextLength.Should().Be(8192);
            models[1].Id.Should().Be("model-a");
            models[1].IsLoaded.Should().BeFalse();
            models[1].ContextLength.Should().BeNull();
        }

        [Fact]
        public async Task ListModelsAsync_NativeApiFailure_KeepsBaselineListing()
        {
            var (client, _) = CreateClient(request =>
                request.RequestUri!.AbsolutePath == "/v1/models"
                    ? StubHttpHandler.Json("""{"data":[{"id":"model-a","object":"model"}]}""")
                    : new HttpResponseMessage(HttpStatusCode.InternalServerError));

            var models = await client.ListModelsAsync();

            models.Should().ContainSingle(m => m.Id == "model-a");
        }

        [Fact]
        public async Task ValidateConnectionAsync_ReturnsFalseWhenUnreachable()
        {
            var (client, _) = CreateClient(_ => throw new HttpRequestException("connection refused"));

            (await client.ValidateConnectionAsync()).Should().BeFalse();
        }

        [Fact]
        public async Task ValidateConnectionAsync_ReturnsTrueWhenServerResponds()
        {
            var (client, _) = CreateClient(_ => StubHttpHandler.Json("""{"data":[]}"""));

            (await client.ValidateConnectionAsync()).Should().BeTrue();
        }

        [Fact]
        public void Capabilities_DisableOpenRouterExtensions()
        {
            var (client, _) = CreateClient(_ => StubHttpHandler.Json("{}"));

            client.Capabilities.SupportsTransforms.Should().BeFalse();
            client.Capabilities.SupportsUsageInclude.Should().BeFalse();
            client.Capabilities.SupportsPricing.Should().BeFalse();
            client.Capabilities.SupportsCaching.Should().BeFalse();
            client.Capabilities.RequiresApiKey.Should().BeFalse();
        }
    }
}
