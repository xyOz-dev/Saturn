using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Saturn.Configuration;
using Saturn.Configuration.Objects;
using Saturn.Providers;
using Xunit;

namespace Saturn.Tests.Providers
{
    [Collection("Configuration")]
    public class ConfigurationManagerRecentWorkspacesTests : IDisposable
    {
        private readonly string _configDir;

        public ConfigurationManagerRecentWorkspacesTests()
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

        [Fact]
        public async Task AddRecentWorkspace_RoundTrips()
        {
            await ConfigurationManager.AddRecentWorkspaceAsync(@"C:\projects\alpha");

            var config = await ConfigurationManager.LoadConfigurationAsync();
            config!.RecentWorkspaces.Should().Equal(@"C:\projects\alpha");
        }

        [Fact]
        public async Task AddRecentWorkspace_DedupesCaseInsensitivelyAndMovesToFront()
        {
            await ConfigurationManager.AddRecentWorkspaceAsync(@"C:\projects\alpha");
            await ConfigurationManager.AddRecentWorkspaceAsync(@"C:\projects\beta");
            await ConfigurationManager.AddRecentWorkspaceAsync(@"C:\PROJECTS\ALPHA");

            var config = await ConfigurationManager.LoadConfigurationAsync();
            config!.RecentWorkspaces.Should().Equal(@"C:\PROJECTS\ALPHA", @"C:\projects\beta");
        }

        [Fact]
        public async Task AddRecentWorkspace_CapsAtTen()
        {
            for (var i = 0; i < 12; i++)
            {
                await ConfigurationManager.AddRecentWorkspaceAsync($@"C:\projects\ws{i}");
            }

            var config = await ConfigurationManager.LoadConfigurationAsync();
            config!.RecentWorkspaces.Should().HaveCount(10);
            config.RecentWorkspaces![0].Should().Be(@"C:\projects\ws11");
            config.RecentWorkspaces.Should().NotContain(@"C:\projects\ws0");
            config.RecentWorkspaces.Should().NotContain(@"C:\projects\ws1");
        }

        [Fact]
        public async Task OtherSaves_PreserveRecentWorkspaces()
        {
            await ConfigurationManager.AddRecentWorkspaceAsync(@"C:\projects\alpha");

            await ConfigurationManager.SaveConfigurationAsync(new PersistedAgentConfiguration
            {
                Name = "Assistant",
                Model = "some-model"
            });

            var config = await ConfigurationManager.LoadConfigurationAsync();
            config!.RecentWorkspaces.Should().Equal(@"C:\projects\alpha");
        }

        [Fact]
        public async Task SecretProtection_PreservesRecentWorkspacesAndEnableSkills()
        {
            // A registered provider with a secret setting forces the
            // WithProtectedSecrets copy path, which must not drop fields.
            var providerName = "recent-ws-prov-" + Guid.NewGuid().ToString("N");
            ProviderRegistry.Register(new FakeProvider
            {
                Name = providerName,
                SettingDescriptors = new[]
                {
                    new ProviderSettingDescriptor
                    {
                        Key = "apiKey",
                        Label = "API Key",
                        Kind = ProviderSettingKind.Secret
                    }
                }
            });

            await ConfigurationManager.AddRecentWorkspaceAsync(@"C:\projects\alpha");

            var config = await ConfigurationManager.LoadConfigurationAsync() ?? new PersistedAgentConfiguration();
            config.EnableSkills = false;
            config.ActiveProvider = providerName;
            config.Providers = new Dictionary<string, PersistedProviderConfiguration>(StringComparer.OrdinalIgnoreCase)
            {
                [providerName] = new()
                {
                    Settings = new Dictionary<string, string?> { ["apiKey"] = "sk-secret" }
                }
            };
            await ConfigurationManager.SaveConfigurationAsync(config);

            var reloaded = await ConfigurationManager.LoadConfigurationAsync();
            reloaded!.RecentWorkspaces.Should().Equal(@"C:\projects\alpha");
            reloaded.EnableSkills.Should().BeFalse();
            reloaded.Providers![providerName].Settings!["apiKey"].Should().StartWith("enc:v1:");
        }
    }
}
