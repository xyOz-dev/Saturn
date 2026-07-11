using System.Collections.Generic;
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
            // No tools attached, so tool_choice must stay off the wire too.
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
    }
}
