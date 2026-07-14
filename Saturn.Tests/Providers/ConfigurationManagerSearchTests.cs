using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Saturn.Configuration;
using Saturn.Configuration.Objects;
using Saturn.Providers;
using Xunit;

namespace Saturn.Tests.Providers
{
    [Collection("Configuration")]
    public class ConfigurationManagerSearchTests : IDisposable
    {
        private readonly string _configDir;

        public ConfigurationManagerSearchTests()
        {
            _configDir = Path.Combine(Path.GetTempPath(), "SaturnSearchTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_configDir);
            Environment.SetEnvironmentVariable("SATURN_CONFIG_DIR", _configDir);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("SATURN_CONFIG_DIR", null);
            try { Directory.Delete(_configDir, recursive: true); } catch { }
        }

        private string ConfigFile => Path.Combine(_configDir, "agent-config.json");

        [Fact]
        public async Task SaveSearchProviderSelection_EncryptsApiKeyAtRest_RoundTripsPlaintext()
        {
            var settings = new ProviderSettings();
            settings.Set("apiKey", "sk-tavily-123");
            await ConfigurationManager.SaveSearchProviderSelectionAsync("tavily", settings);

            var raw = await File.ReadAllTextAsync(ConfigFile);
            raw.Should().NotContain("sk-tavily-123");
            raw.Should().Contain("enc:v1:");

            var config = await ConfigurationManager.LoadConfigurationAsync();
            config!.SearchProvider.Should().Be("tavily");
            config.SearchProviders!["tavily"].Settings!["apiKey"].Should().StartWith("enc:v1:");

            ConfigurationManager.GetSearchProviderSettings(config, "tavily").Get("apiKey").Should().Be("sk-tavily-123");
        }

        [Fact]
        public async Task SearchConfig_SurvivesSaveThatProtectsAnLlmSecret()
        {
            // Regression for the WithProtectedSecrets copy-constructor: a save that encrypts an
            // LLM secret must not drop the search block.
            var searchSettings = new ProviderSettings();
            searchSettings.Set("apiKey", "sk-brave-xyz");
            await ConfigurationManager.SaveSearchProviderSelectionAsync("brave", searchSettings);

            var llmSettings = new ProviderSettings();
            llmSettings.Set("apiKey", "sk-openrouter-abc");
            await ConfigurationManager.SaveProviderSelectionAsync("openrouter", llmSettings, "anthropic/claude-sonnet-4");

            var reloaded = await ConfigurationManager.LoadConfigurationAsync();
            reloaded!.SearchProvider.Should().Be("brave");
            ConfigurationManager.GetSearchProviderSettings(reloaded, "brave").Get("apiKey").Should().Be("sk-brave-xyz");
            ConfigurationManager.GetProviderSettings(reloaded, "openrouter").Get("apiKey").Should().Be("sk-openrouter-abc");
        }

        [Fact]
        public async Task SaveConfiguration_WithNullSearchFields_PreservesExistingSearchBlock()
        {
            var searchSettings = new ProviderSettings();
            searchSettings.Set("apiKey", "sk-serper-1");
            await ConfigurationManager.SaveSearchProviderSelectionAsync("serper", searchSettings);

            // A routine agent-config save (e.g. temperature change) leaves search fields null.
            await ConfigurationManager.SaveConfigurationAsync(new PersistedAgentConfiguration
            {
                Name = "Assistant",
                Model = "some-model"
            });

            var reloaded = await ConfigurationManager.LoadConfigurationAsync();
            reloaded!.SearchProvider.Should().Be("serper");
            ConfigurationManager.GetSearchProviderSettings(reloaded, "serper").Get("apiKey").Should().Be("sk-serper-1");
        }
    }
}
