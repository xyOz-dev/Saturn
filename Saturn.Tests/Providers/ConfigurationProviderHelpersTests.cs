using System;
using System.Collections.Generic;
using FluentAssertions;
using Saturn.Configuration;
using Saturn.Configuration.Objects;
using Xunit;

namespace Saturn.Tests.Providers
{
    public class ConfigurationProviderHelpersTests
    {
        [Fact]
        public void GetProviderSettings_ReturnsSavedValues()
        {
            var config = new PersistedAgentConfiguration
            {
                Providers = new Dictionary<string, PersistedProviderConfiguration>
                {
                    ["lmstudio"] = new()
                    {
                        Settings = new Dictionary<string, string?> { ["baseUrl"] = "http://box:5000/v1" }
                    }
                }
            };

            var settings = ConfigurationManager.GetProviderSettings(config, "lmstudio");

            settings.Get("baseUrl").Should().Be("http://box:5000/v1");
        }

        [Fact]
        public void GetProviderSettings_UnknownProvider_ReturnsEmptySettings()
        {
            ConfigurationManager.GetProviderSettings(null, "lmstudio").Values.Should().BeEmpty();
            ConfigurationManager.GetProviderSettings(new PersistedAgentConfiguration(), "lmstudio").Values.Should().BeEmpty();
        }

        [Fact]
        public void GetProviderModel_PrefersPerProviderMemory()
        {
            var config = new PersistedAgentConfiguration
            {
                ActiveProvider = "openrouter",
                Model = "anthropic/claude-sonnet-4",
                Providers = new Dictionary<string, PersistedProviderConfiguration>
                {
                    ["lmstudio"] = new() { Model = "qwen2.5-coder-32b" }
                }
            };

            ConfigurationManager.GetProviderModel(config, "lmstudio").Should().Be("qwen2.5-coder-32b");
        }

        [Fact]
        public void GetProviderModel_FlatModelOnlyCountsForItsOwnProvider()
        {
            var config = new PersistedAgentConfiguration
            {
                ActiveProvider = "openrouter",
                Model = "anthropic/claude-sonnet-4"
            };

            // The flat legacy field belonged to OpenRouter, so it must not leak into
            // another provider's model resolution.
            ConfigurationManager.GetProviderModel(config, "openrouter").Should().Be("anthropic/claude-sonnet-4");
            ConfigurationManager.GetProviderModel(config, "lmstudio").Should().BeNull();
        }
    }
}
