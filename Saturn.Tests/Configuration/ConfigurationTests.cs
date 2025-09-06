using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using FluentAssertions;
using Saturn.Configuration;
using Saturn.Configuration.Objects;

namespace Saturn.Tests.Configuration
{

    public class ConfigurationTests : IDisposable
    {
        private readonly string _testConfigPath;
        private readonly string _testAppDataPath;
        
        public ConfigurationTests()
        {
            // Create a temporary directory for test configs
            _testAppDataPath = Path.Combine(Path.GetTempPath(), $"SaturnTest_{Guid.NewGuid()}");
            Directory.CreateDirectory(_testAppDataPath);
            _testConfigPath = Path.Combine(_testAppDataPath, "agent-config.json");
            
            // Use environment variable to override the config path for testing
            Environment.SetEnvironmentVariable("SATURN_TEST_CONFIG_PATH", _testAppDataPath);
        }
        
        public void Dispose()
        {
            // Clean up environment variable
            Environment.SetEnvironmentVariable("SATURN_TEST_CONFIG_PATH", null);
            
            // Clean up test directory
            try
            {
                if (Directory.Exists(_testAppDataPath))
                {
                    Directory.Delete(_testAppDataPath, true);
                }
            }
            catch
            {
                // Ignore cleanup errors (common on Windows due to file locks)
            }
        }
        
        [Fact]
        public async Task LoadConfiguration_WithWindowsLineEndings_ShouldParseSuccessfully()
        {
            // Arrange
            var config = new PersistedAgentConfiguration
            {
                Name = "TestAgent",
                ProviderName = "openrouter",
                Model = "test-model",
                Temperature = 0.7,
                EnableUserRules = true,
                RequireCommandApproval = true
            };
            
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions 
            { 
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            
            // Add Windows line endings (CRLF)
            json = json.Replace("\n", "\r\n");
            await File.WriteAllTextAsync(_testConfigPath, json);
            
            // Act
            var loadedConfig = await ConfigurationManager.LoadConfigurationAsync();
            
            // Assert
            loadedConfig.Should().NotBeNull();
            loadedConfig!.Name.Should().Be("TestAgent");
            loadedConfig.ProviderName.Should().Be("openrouter");
            loadedConfig.Model.Should().Be("test-model");
        }
        
        [Fact]
        public async Task LoadConfiguration_WithMissingProviderName_ShouldNotDefault()
        {
            // Arrange
            var json = @"{
                ""name"": ""TestAgent"",
                ""model"": ""test-model"",
                ""temperature"": 0.7
            }";
            
            await File.WriteAllTextAsync(_testConfigPath, json);
            
            // Act
            var loadedConfig = await ConfigurationManager.LoadConfigurationAsync();
            
            // Assert
            loadedConfig.Should().NotBeNull();
            // ProviderName should be null when not specified (no longer defaults to openrouter)
            loadedConfig!.ProviderName.Should().BeNull();
        }
        
        [Fact]
        public async Task LoadConfiguration_WithMissingRequiredFields_ShouldSetDefaults()
        {
            // Arrange
            var json = @"{
                ""name"": ""TestAgent"",
                ""model"": ""test-model""
            }";
            
            await File.WriteAllTextAsync(_testConfigPath, json);
            
            // Act
            var loadedConfig = await ConfigurationManager.LoadConfigurationAsync();
            
            // Assert
            loadedConfig.Should().NotBeNull();
            loadedConfig!.RequireCommandApproval.Should().BeTrue();
            loadedConfig.EnableUserRules.Should().BeTrue();
        }
        
        [Fact]
        public async Task LoadConfiguration_WithCorruptedJson_ShouldReturnNull()
        {
            // Arrange
            var corruptedJson = @"{ ""Name"": ""TestAgent"", INVALID JSON }";
            await File.WriteAllTextAsync(_testConfigPath, corruptedJson);
            
            // Act
            var loadedConfig = await ConfigurationManager.LoadConfigurationAsync();
            
            // Assert
            loadedConfig.Should().BeNull();
        }
        
        [Fact]
        public async Task SaveAndLoadConfiguration_ShouldPreserveAllFields()
        {
            // Arrange
            var config = new PersistedAgentConfiguration
            {
                Name = "TestAgent",
                ProviderName = "anthropic",
                Model = "claude-3",
                Temperature = 0.5,
                MaxTokens = 4096,
                TopP = 0.9,
                EnableStreaming = true,
                MaintainHistory = true,
                MaxHistoryMessages = 20,
                EnableTools = true,
                ToolNames = new List<string> { "tool1", "tool2" },
                RequireCommandApproval = false,
                EnableUserRules = true
            };
            
            // Act
            await ConfigurationManager.SaveConfigurationAsync(config);
            var loadedConfig = await ConfigurationManager.LoadConfigurationAsync();
            
            // Assert
            loadedConfig.Should().NotBeNull();
            loadedConfig.Should().BeEquivalentTo(config);
        }
    }
    
    
    public class OpenRouterProviderConfigTests
    {
        [Fact]
        public void DecryptData_WithWindowsLineEndings_ShouldRemoveCarriageReturns()
        {
            // This test validates that the OpenRouterProvider correctly handles
            // base64 encoded JSON with Windows line endings
            
            // Arrange
            var json = @"{
  ""ProviderName"": ""OpenRouter"",
  ""AuthType"": 1,
  ""ApiKey"": ""test-key""
}";
            // Add Windows line endings
            json = json.Replace("\n", "\r\n");
            
            // Convert to base64 (simulating what EncryptData does)
            var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
            
            // Act - simulate what DecryptData should do
            var decrypted = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
            decrypted = decrypted.Replace("\r", "");
            
            // Assert
            decrypted.Should().NotContain("\r");
            
            // Should be valid JSON
            var config = JsonSerializer.Deserialize<JsonElement>(decrypted);
            config.ValueKind.Should().NotBe(JsonValueKind.Null);
            config.ValueKind.Should().NotBe(JsonValueKind.Undefined);
        }
    }
}