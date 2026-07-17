using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Saturn.Agents.Core;
using Saturn.OpenRouter;
using Saturn.OpenRouter.Errors;
using Saturn.OpenRouter.Models.Api.Chat;
using Saturn.Providers;
using Xunit;

namespace Saturn.Tests.Providers
{
    public class AgentProviderRetryTests
    {
        private static TestAgent CreateAgent(FakeLlmClient client, bool enableStreaming = false)
        {
            var config = new AgentConfiguration
            {
                Name = "Test",
                SystemPrompt = "You are a test agent.",
                ClientSource = new StaticClientSource(client, "test-provider"),
                Model = "test-model",
                MaintainHistory = true,
                EnableStreaming = enableStreaming
            };
            return new TestAgent(config);
        }

        private static OpenRouterException RateLimitError(TimeSpan? retryAfter = null)
            => new(HttpStatusCode.TooManyRequests,
                "Provider returned error | provider=Google | raw=RESOURCE_EXHAUSTED",
                apiErrorCode: 429,
                retryAfter: retryAfter);

        [Fact]
        public async Task Execute_RateLimited_RetriesAndCompletesTheTurn()
        {
            var client = new FakeLlmClient();
            client.ChatExceptionQueue.Enqueue(RateLimitError());
            client.ChatExceptionQueue.Enqueue(RateLimitError());
            var agent = CreateAgent(client);

            var result = await agent.Execute<Message>("hello");

            result.Content.GetString().Should().Be("ok");
            client.Requests.Should().HaveCount(3);
            agent.RetryWaits.Should().Equal(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4));
            agent.ChatHistory.Select(m => m.Role).Should().Equal("system", "user", "assistant");
        }

        [Fact]
        public async Task Execute_RateLimitedWithRetryAfter_WaitsTheAdvertisedDuration()
        {
            var client = new FakeLlmClient();
            client.ChatExceptionQueue.Enqueue(RateLimitError(TimeSpan.FromSeconds(7)));
            var agent = CreateAgent(client);

            await agent.Execute<Message>("hello");

            agent.RetryWaits.Should().Equal(TimeSpan.FromSeconds(7));
        }

        [Fact]
        public async Task Execute_TransientProviderError_IsRetried()
        {
            var client = new FakeLlmClient();
            client.ChatExceptionQueue.Enqueue(new OpenRouterException(HttpStatusCode.BadGateway, "Provider returned error"));
            var agent = CreateAgent(client);

            var result = await agent.Execute<Message>("hello");

            result.Content.GetString().Should().Be("ok");
            client.Requests.Should().HaveCount(2);
        }

        [Fact]
        public async Task Execute_ExpiredGeminiCacheContent_IsRetried()
        {
            var client = new FakeLlmClient();
            client.ChatExceptionQueue.Enqueue(new OpenRouterException(
                HttpStatusCode.BadRequest,
                "Provider returned error | provider=Google | raw={ \"error\": { \"code\": 400, \"message\": \"Cache content 8114606996528824320 is expired.\", \"status\": \"INVALID_ARGUMENT\" } }",
                apiErrorCode: 400));
            var agent = CreateAgent(client);

            var result = await agent.Execute<Message>("hello");

            result.Content.GetString().Should().Be("ok");
            client.Requests.Should().HaveCount(2);
        }

        [Fact]
        public async Task Execute_CloudflareEdgeTimeout524_IsRetried()
        {
            var client = new FakeLlmClient();
            // A stalled upstream surfaces as a bare Cloudflare status with no JSON body.
            client.ChatExceptionQueue.Enqueue(new OpenRouterException((HttpStatusCode)524, "error code: 524"));
            var agent = CreateAgent(client);

            var result = await agent.Execute<Message>("hello");

            result.Content.GetString().Should().Be("ok");
            client.Requests.Should().HaveCount(2);
            agent.RetryWaits.Should().HaveCount(1);
        }

        [Fact]
        public async Task Execute_GatewayTimeout504_IsRetried()
        {
            var client = new FakeLlmClient();
            client.ChatExceptionQueue.Enqueue(new OpenRouterException(HttpStatusCode.GatewayTimeout, "Provider returned error"));
            var agent = CreateAgent(client);

            var result = await agent.Execute<Message>("hello");

            result.Content.GetString().Should().Be("ok");
            client.Requests.Should().HaveCount(2);
        }

        [Fact]
        public async Task Execute_NonRetryableError_ThrowsWithoutRetrying()
        {
            var client = new FakeLlmClient();
            client.ChatExceptionQueue.Enqueue(new OpenRouterException(HttpStatusCode.Unauthorized, "Invalid API key"));
            var agent = CreateAgent(client);

            await agent.Invoking(a => a.Execute<Message>("hello"))
                .Should().ThrowAsync<OpenRouterException>();

            client.Requests.Should().HaveCount(1);
            agent.RetryWaits.Should().BeEmpty();
        }

        [Fact]
        public async Task Execute_RateLimitNeverClears_GivesUpAfterMaxAttempts()
        {
            var client = new FakeLlmClient();
            // One more failure than the 50-attempt retry budget.
            for (var i = 0; i < 51; i++)
            {
                client.ChatExceptionQueue.Enqueue(RateLimitError());
            }
            var agent = CreateAgent(client);

            await agent.Invoking(a => a.Execute<Message>("hello"))
                .Should().ThrowAsync<OpenRouterException>();

            client.Requests.Should().HaveCount(51);
        }

        [Fact]
        public async Task ExecuteStream_RateLimited_RetriesAndKeepsTheTurn()
        {
            var client = new FakeLlmClient();
            client.StreamExceptionQueue.Enqueue(RateLimitError());
            client.StreamChunks.Add(new ChatCompletionChunk
            {
                Choices = new[] { new StreamingChoice { Delta = new Delta { Role = "assistant", Content = "hello back" } } }
            });
            client.StreamChunks.Add(new ChatCompletionChunk
            {
                Choices = new[] { new StreamingChoice { Delta = new Delta(), FinishReason = "stop" } }
            });
            var agent = CreateAgent(client, enableStreaming: true);

            var chunks = new List<StreamChunk>();
            var result = await agent.ExecuteStreamAsync("hello", chunk =>
            {
                if (!string.IsNullOrEmpty(chunk.Content))
                {
                    chunks.Add(chunk);
                }
                return Task.CompletedTask;
            });

            result.Content.GetString().Should().Be("hello back");
            client.Requests.Should().HaveCount(2);
            agent.RetryWaits.Should().HaveCount(1);
            // The retry notice is display-only status; the real content is not.
            chunks.Should().Contain(c => c.Content!.Contains("retrying") && c.IsTransientNotice);
            chunks.Should().Contain(c => c.Content == "hello back" && !c.IsTransientNotice);
            agent.ChatHistory.Select(m => m.Role).Should().Equal("system", "user", "assistant");
            agent.ChatHistory[^1].Content.GetString().Should().Be("hello back");
        }

        [Fact]
        public async Task ExecuteStream_ProviderRejectsStreaming_EmitsFallbackTextAsContent()
        {
            var client = new FakeLlmClient();
            client.StreamExceptionQueue.Enqueue(new OpenRouterException(
                HttpStatusCode.BadRequest, "Streaming is unsupported for this model"));
            var agent = CreateAgent(client, enableStreaming: true);

            var chunks = new List<StreamChunk>();
            var result = await agent.ExecuteStreamAsync("hello", chunk =>
            {
                chunks.Add(chunk);
                return Task.CompletedTask;
            });

            // The non-streaming fallback answered; its text must reach the consumer
            // as a real content chunk, not just the transient fallback notice.
            result.Content.GetString().Should().Be("ok");
            chunks.Should().Contain(c => c.Content == "ok" && !c.IsTransientNotice);
            chunks.Should().Contain(c => c.IsTransientNotice && c.Content!.Contains("falling back"));
        }

        [Fact]
        public async Task ExecuteStream_MidStreamError_ResetsPartialOutputBeforeRetrying()
        {
            var client = new FakeLlmClient();
            // First attempt streams a partial token, then fails mid-stream.
            client.PreErrorStreamChunks.Add(new ChatCompletionChunk
            {
                Choices = new[] { new StreamingChoice { Delta = new Delta { Role = "assistant", Content = "partial " } } }
            });
            client.StreamExceptionQueue.Enqueue(RateLimitError());
            // Retry streams the full response.
            client.StreamChunks.Add(new ChatCompletionChunk
            {
                Choices = new[] { new StreamingChoice { Delta = new Delta { Role = "assistant", Content = "the full answer" } } }
            });
            client.StreamChunks.Add(new ChatCompletionChunk
            {
                Choices = new[] { new StreamingChoice { Delta = new Delta(), FinishReason = "stop" } }
            });
            var agent = CreateAgent(client, enableStreaming: true);

            var sawReset = false;
            var contentAfterReset = new StringBuilder();
            var result = await agent.ExecuteStreamAsync("hello", chunk =>
            {
                if (chunk.ResetContent)
                {
                    sawReset = true;
                    contentAfterReset.Clear();
                }
                else if (!chunk.IsToolCall && !string.IsNullOrEmpty(chunk.Content))
                {
                    contentAfterReset.Append(chunk.Content);
                }
                return Task.CompletedTask;
            });

            sawReset.Should().BeTrue();
            result.Content.GetString().Should().Be("the full answer");
            // A consumer that honors ResetContent must not retain the discarded "partial ".
            contentAfterReset.ToString().Should().NotContain("partial ");
            contentAfterReset.ToString().Should().Contain("the full answer");
        }

        [Fact]
        public async Task StreamAsync_MidStreamErrorEvent_SurfacesAsOpenRouterException()
        {
            var sse =
                "data: {\"id\":\"1\",\"choices\":[{\"delta\":{\"role\":\"assistant\",\"content\":\"partial\"}}]}\n\n" +
                "data: {\"error\":{\"code\":429,\"message\":\"Provider returned error\",\"metadata\":{\"provider_name\":\"Google\",\"raw\":\"RESOURCE_EXHAUSTED\"}}}\n\n" +
                "data: [DONE]\n\n";

            var log = new List<(HttpRequestMessage, string)>();
            var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(sse, Encoding.UTF8, "text/event-stream")
            }, log);

            using var client = new OpenRouterClient(new OpenRouterOptions { ApiKey = "test-key", HttpMessageHandler = handler });

            var chunks = new List<ChatCompletionChunk>();
            var act = async () =>
            {
                await foreach (var chunk in client.ChatStreaming.StreamAsync(new ChatCompletionRequest { Model = "m" }))
                {
                    chunks.Add(chunk);
                }
            };

            var ex = await act.Should().ThrowAsync<OpenRouterException>();
            ex.Which.ApiErrorCode.Should().Be(429);
            ex.Which.Message.Should().Contain("provider=Google");
            chunks.Should().HaveCount(1);
        }

        [Fact]
        public async Task ChatAsync_429WithRetryAfterHeader_ExposesRetryAfterOnException()
        {
            var log = new List<(HttpRequestMessage, string)>();
            var handler = new StubHttpHandler(_ =>
            {
                var response = StubHttpHandler.Json(
                    "{\"error\":{\"code\":429,\"message\":\"Provider returned error\"}}",
                    HttpStatusCode.TooManyRequests);
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(5));
                return response;
            }, log);

            using var client = new OpenRouterClient(new OpenRouterOptions { ApiKey = "test-key", HttpMessageHandler = handler });

            var act = async () => await client.Chat.CreateAsync(new ChatCompletionRequest { Model = "m" });

            var ex = await act.Should().ThrowAsync<OpenRouterException>();
            ex.Which.RetryAfter.Should().Be(TimeSpan.FromSeconds(5));
            ex.Which.ApiErrorCode.Should().Be(429);
        }
    }
}
