using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Saturn.Agents.Core;
using Saturn.OpenRouter.Models.Api.Chat;
using Saturn.Providers;
using Xunit;

namespace Saturn.Tests.Providers
{
    public class AgentRequestShapingTests
    {
        private static TestAgent CreateAgent(LlmClientCapabilities capabilities, FakeLlmClient client, bool enableTools = false, List<string>? toolNames = null)
        {
            client.Capabilities = capabilities;
            var config = new AgentConfiguration
            {
                Name = "Test",
                SystemPrompt = "You are a test agent.",
                ClientSource = new StaticClientSource(client, "test-provider"),
                Model = "test-model",
                EnableTools = enableTools,
                ToolNames = toolNames ?? new List<string>(),
                MaintainHistory = true
            };
            return new TestAgent(config);
        }

        [Fact]
        public async Task Execute_ProviderWithoutExtensions_OmitsTransformsUsageAndToolChoice()
        {
            var client = new FakeLlmClient();
            var agent = CreateAgent(new LlmClientCapabilities
            {
                SupportsTransforms = false,
                SupportsUsageInclude = false,
                SupportsToolChoice = true
            }, client);

            await agent.Execute<Message>("hello");

            client.LastRequest.Should().NotBeNull();
            client.LastRequest!.Transforms.Should().BeNull();
            client.LastRequest.Usage.Should().BeNull();
            client.LastRequest.ToolChoice.Should().BeNull();
            client.LastRequest.Tools.Should().BeNull();
        }

        [Fact]
        public async Task Execute_ProviderWithExtensions_SendsTransformsAndUsage()
        {
            var client = new FakeLlmClient();
            var agent = CreateAgent(new LlmClientCapabilities
            {
                SupportsTransforms = true,
                SupportsUsageInclude = true,
                SupportsToolChoice = true
            }, client);

            await agent.Execute<Message>("hello");

            client.LastRequest!.Transforms.Should().BeEquivalentTo(new[] { "middle-out" });
            client.LastRequest.Usage.Should().NotBeNull();
        }

        [Fact]
        public async Task Execute_ToolsEnabled_SendsToolsAndToolChoice()
        {
            var client = new FakeLlmClient();
            var agent = CreateAgent(new LlmClientCapabilities { SupportsToolChoice = true }, client,
                enableTools: true, toolNames: new List<string> { "read_file" });

            await agent.Execute<Message>("hello");

            client.LastRequest!.Tools.Should().NotBeNullOrEmpty();
            client.LastRequest.ToolChoice.Should().NotBeNull();
        }

        [Theory]
        [InlineData("")]
        [InlineData("{\"patchText\": \"truncated mid-str")]
        public async Task Execute_ToolCallWithUnparseableArguments_IsSanitizedBeforeItPoisonsHistory(string brokenArguments)
        {
            var client = new FakeLlmClient();
            client.ResponseQueue.Enqueue(new ChatCompletionResponse
            {
                Choices = new[]
                {
                    new Choice
                    {
                        Message = new AssistantMessageResponse
                        {
                            Role = "assistant",
                            ToolCalls = new[]
                            {
                                new ToolCall
                                {
                                    Id = "call_1",
                                    Type = "function",
                                    Function = new ToolCall.FunctionCall { Name = "read_file", Arguments = brokenArguments }
                                }
                            }
                        },
                        FinishReason = "length"
                    }
                }
            });

            var agent = CreateAgent(new LlmClientCapabilities { SupportsToolChoice = true }, client,
                enableTools: true, toolNames: new List<string> { "read_file" });

            await agent.Execute<Message>("hello");

            client.Requests.Should().HaveCountGreaterThan(1);
            var followUp = client.Requests[^1];
            var assistantToolMessage = followUp.Messages!.First(m => m.Role == "assistant" && m.ToolCalls != null);
            assistantToolMessage.ToolCalls![0].Function!.Arguments.Should().Be("{}");
        }

        [Fact]
        public void SystemPrompt_CachingUnsupported_UsesPlainStringContent()
        {
            var agent = CreateAgent(new LlmClientCapabilities { SupportsCaching = false }, new FakeLlmClient());

            agent.ChatHistory[0].Role.Should().Be("system");
            agent.ChatHistory[0].Content.ValueKind.Should().Be(JsonValueKind.String);
        }

        [Fact]
        public void SystemPrompt_CachingSupported_UsesCacheControlContentParts()
        {
            var agent = CreateAgent(new LlmClientCapabilities { SupportsCaching = true }, new FakeLlmClient());

            agent.ChatHistory[0].Role.Should().Be("system");
            agent.ChatHistory[0].Content.ValueKind.Should().Be(JsonValueKind.Array);
        }

        [Fact]
        public async Task Execute_AfterProviderSwap_RoutesToTheNewClient()
        {
            var nameA = "shape-a-" + Guid.NewGuid().ToString("N");
            var nameB = "shape-b-" + Guid.NewGuid().ToString("N");
            var clientA = new FakeLlmClient();
            var clientB = new FakeLlmClient();
            ProviderRegistry.Register(new FakeProvider { Name = nameA, Factory = _ => clientA });
            ProviderRegistry.Register(new FakeProvider { Name = nameB, Factory = _ => clientB });

            var manager = new LlmClientManager();
            (await manager.SwapAsync(nameA, new ProviderSettings())).Success.Should().BeTrue();

            var agent = new TestAgent(new AgentConfiguration
            {
                Name = "Test",
                SystemPrompt = "You are a test agent.",
                ClientSource = manager,
                Model = "test-model"
            });

            await agent.Execute<Message>("one");
            clientA.LastRequest.Should().NotBeNull();
            clientB.LastRequest.Should().BeNull();

            (await manager.SwapAsync(nameB, new ProviderSettings())).Success.Should().BeTrue();

            await agent.Execute<Message>("two");
            clientB.LastRequest.Should().NotBeNull("the turn after a swap must resolve the new client");
        }

        [Fact]
        public async Task Execute_AfterSwapToNonCachingProvider_StripsCacheControlFromHistory()
        {
            var cachingClient = new FakeLlmClient();
            var agent = CreateAgent(new LlmClientCapabilities { SupportsCaching = true }, cachingClient);
            agent.ChatHistory[0].Content.ValueKind.Should().Be(JsonValueKind.Array);

            var plainClient = new FakeLlmClient { Capabilities = new LlmClientCapabilities { SupportsCaching = false } };
            agent.Configuration.ClientSource = new StaticClientSource(plainClient, "swapped");

            await agent.Execute<Message>("hello");

            plainClient.LastRequest.Should().NotBeNull();
            var systemMessage = plainClient.LastRequest!.Messages![0];
            systemMessage.Role.Should().Be("system");
            systemMessage.Content.ValueKind.Should().Be(JsonValueKind.String);

            agent.ChatHistory[0].Content.ValueKind.Should().Be(JsonValueKind.Array);
        }
    }
}
