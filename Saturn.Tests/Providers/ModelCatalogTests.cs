using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Saturn.Providers;
using Xunit;

namespace Saturn.Tests.Providers
{
    public class ModelCatalogTests
    {
        private static StaticClientSource Source(FakeLlmClient client)
            => new(client, "catalog-" + Guid.NewGuid().ToString("N"));

        [Fact]
        public async Task ResolveModelAsync_RequestedModelAvailable_ReturnsIt()
        {
            var client = new FakeLlmClient
            {
                Models = new List<ModelInfo> { new() { Id = "model-a" }, new() { Id = "model-b" } }
            };

            var resolved = await ModelCatalog.ResolveModelAsync(Source(client), "model-b");

            resolved.Should().Be("model-b");
        }

        [Fact]
        public async Task ResolveModelAsync_MissingModel_PrefersFallbackThenDefaultThenLoaded()
        {
            var client = new FakeLlmClient
            {
                Models = new List<ModelInfo>
                {
                    new() { Id = "model-a" },
                    new() { Id = "parent-model" }
                }
            };

            var withFallback = await ModelCatalog.ResolveModelAsync(Source(client), "unavailable", "parent-model");
            withFallback.Should().Be("parent-model");

            var loadedClient = new FakeLlmClient
            {
                Models = new List<ModelInfo>
                {
                    new() { Id = "model-a" },
                    new() { Id = "model-loaded", IsLoaded = true }
                }
            };

            var withLoaded = await ModelCatalog.ResolveModelAsync(Source(loadedClient), "unavailable");
            withLoaded.Should().Be("model-loaded");
        }

        [Fact]
        public async Task ResolveModelAsync_EmptyListing_ReturnsRequestedUnchanged()
        {
            var client = new FakeLlmClient { Models = new List<ModelInfo>() };

            var resolved = await ModelCatalog.ResolveModelAsync(Source(client), "whatever");

            resolved.Should().Be("whatever");
        }

        [Fact]
        public async Task GetAsync_EmptyLiveListing_FallsBackToCapabilityModels()
        {
            var client = new FakeLlmClient
            {
                Models = new List<ModelInfo>(),
                Capabilities = new LlmClientCapabilities
                {
                    FallbackModels = new[] { new ModelInfo { Id = "fallback-model" } }
                }
            };

            var models = await ModelCatalog.GetAsync(Source(client));

            models.Should().ContainSingle(m => m.Id == "fallback-model");
        }

        [Fact]
        public async Task GetAsync_DisconnectedSource_ReturnsEmpty()
        {
            var models = await ModelCatalog.GetAsync(null!);
            models.Should().BeEmpty();
        }
    }
}
