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
    public class ConfigurationManagerFileTests : IDisposable
    {
        private readonly string _configDir;

        public ConfigurationManagerFileTests()
        {
            _configDir = Path.Combine(Path.GetTempPath(), "SaturnTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_configDir);
            Environment.SetEnvironmentVariable("SATURN_CONFIG_DIR", _configDir);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("SATURN_CONFIG_DIR", null);
            try
            {
                Directory.Delete(_configDir, recursive: true);
            }
            catch
            {
            }
        }

        private string ConfigFile => Path.Combine(_configDir, "agent-config.json");

        [Fact]
        public async Task Load_LegacyConfig_MigratesToProviderShape()
        {
            await File.WriteAllTextAsync(ConfigFile,
                """{"name":"Assistant","model":"anthropic/claude-sonnet-4","temperature":0.15,"enableStreaming":true}""");

            var config = await ConfigurationManager.LoadConfigurationAsync();

            config.Should().NotBeNull();
            config!.ActiveProvider.Should().Be("openrouter");
            config.Providers.Should().ContainKey("openrouter");
            config.Providers!["openrouter"].Model.Should().Be("anthropic/claude-sonnet-4");
            config.Model.Should().Be("anthropic/claude-sonnet-4");
        }

        [Fact]
        public async Task Save_WithoutProviderFields_PreservesProvidersBlockOnDisk()
        {
            var seeded = new PersistedAgentConfiguration
            {
                Name = "Assistant",
                Model = "qwen2.5-coder-32b",
                ActiveProvider = "lmstudio",
                Providers = new Dictionary<string, PersistedProviderConfiguration>(StringComparer.OrdinalIgnoreCase)
                {
                    ["lmstudio"] = new()
                    {
                        Model = "qwen2.5-coder-32b",
                        Settings = new Dictionary<string, string?> { ["baseUrl"] = "http://box:5000/v1" }
                    },
                    ["openrouter"] = new() { Model = "anthropic/claude-sonnet-4" }
                }
            };
            await ConfigurationManager.SaveConfigurationAsync(seeded);

            await ConfigurationManager.SaveConfigurationAsync(new PersistedAgentConfiguration
            {
                Name = "Assistant",
                Model = "new-local-model"
            });

            var reloaded = await ConfigurationManager.LoadConfigurationAsync();

            reloaded!.ActiveProvider.Should().Be("lmstudio");
            reloaded.Providers.Should().ContainKey("openrouter");
            reloaded.Providers!["openrouter"].Model.Should().Be("anthropic/claude-sonnet-4");
            reloaded.Providers["lmstudio"].Settings!["baseUrl"].Should().Be("http://box:5000/v1");
            reloaded.Providers["lmstudio"].Model.Should().Be("new-local-model");
        }

        [Fact]
        public async Task Load_ProvidersKeys_AreCaseInsensitiveAfterRoundTrip()
        {
            await File.WriteAllTextAsync(ConfigFile,
                """{"activeProvider":"lmstudio","providers":{"lmstudio":{"model":"qwen2.5-coder-32b"}}}""");

            var config = await ConfigurationManager.LoadConfigurationAsync();

            ConfigurationManager.GetProviderModel(config, "LMStudio").Should().Be("qwen2.5-coder-32b");
        }

        [Fact]
        public async Task Load_CaseCollidingProviderKeys_CollapsesInsteadOfDroppingConfig()
        {
            await File.WriteAllTextAsync(ConfigFile,
                """{"activeProvider":"lmstudio","model":"first-model","providers":{"lmstudio":{"model":"first-model"},"LMStudio":{"model":"second-model"}}}""");

            var config = await ConfigurationManager.LoadConfigurationAsync();

            config.Should().NotBeNull();
            config!.Providers.Should().HaveCount(1);
            ConfigurationManager.GetProviderModel(config, "lmstudio").Should().Be("first-model");
        }

        [Fact]
        public async Task SaveProviderSelection_SecretMatchingEnvironment_IsNotPersisted()
        {
            var providerName = "secretprov-" + Guid.NewGuid().ToString("N");
            var envVar = "SATURN_TEST_KEY_" + Guid.NewGuid().ToString("N");
            Environment.SetEnvironmentVariable(envVar, "sk-from-env");
            try
            {
                ProviderRegistry.Register(new FakeProvider
                {
                    Name = providerName,
                    SettingDescriptors = new[]
                    {
                        new ProviderSettingDescriptor
                        {
                            Key = "apiKey",
                            Label = "API Key",
                            Kind = ProviderSettingKind.Secret,
                            EnvironmentVariable = envVar
                        }
                    }
                });

                var mirrored = new ProviderSettings();
                mirrored.Set("apiKey", "sk-from-env");
                await ConfigurationManager.SaveProviderSelectionAsync(providerName, mirrored);

                var config = await ConfigurationManager.LoadConfigurationAsync();
                config!.Providers![providerName].Settings.Should().NotContainKey("apiKey");

                var custom = new ProviderSettings();
                custom.Set("apiKey", "sk-custom");
                await ConfigurationManager.SaveProviderSelectionAsync(providerName, custom);

                config = await ConfigurationManager.LoadConfigurationAsync();
                config!.Providers![providerName].Settings!["apiKey"].Should().Be("sk-custom");
            }
            finally
            {
                Environment.SetEnvironmentVariable(envVar, null);
            }
        }

        [Fact]
        public async Task Save_LeavesNoTempFileBehind()
        {
            await ConfigurationManager.SaveConfigurationAsync(new PersistedAgentConfiguration { Name = "A", Model = "m" });

            File.Exists(ConfigFile).Should().BeTrue();
            File.Exists(ConfigFile + ".tmp").Should().BeFalse();
        }
    }
}
