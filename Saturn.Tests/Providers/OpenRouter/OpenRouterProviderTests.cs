using Xunit;
using FluentAssertions;
using Saturn.Providers.OpenRouter;
using Saturn.Providers;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Saturn.Tests.Providers.OpenRouter
{
    public class OpenRouterProviderTests
    {
        [Fact]
        public void Constructor_SetsProperties()
        {
            // Act
            var provider = new OpenRouterProvider();
            
            // Assert
            provider.Name.Should().Be("OpenRouter");
            provider.Description.Should().NotBeNullOrEmpty();
            provider.AuthType.Should().Be(AuthenticationType.ApiKey);
        }
        
        [Fact]
        public void IsAuthenticated_WithoutApiKey_ReturnsFalse()
        {
            // Arrange
            var originalKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
            Environment.SetEnvironmentVariable("OPENROUTER_API_KEY", null);
            
            try
            {
                var provider = new OpenRouterProvider();
                
                // Assert
                provider.IsAuthenticated.Should().BeFalse();
            }
            finally
            {
                Environment.SetEnvironmentVariable("OPENROUTER_API_KEY", originalKey);
            }
        }
        
        [Fact]
        public void IsAuthenticated_WithApiKey_ReturnsTrue()
        {
            // Arrange
            var originalKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
            Environment.SetEnvironmentVariable("OPENROUTER_API_KEY", "test_key");
            
            try
            {
                var provider = new OpenRouterProvider();
                
                // Assert
                provider.IsAuthenticated.Should().BeTrue();
            }
            finally
            {
                Environment.SetEnvironmentVariable("OPENROUTER_API_KEY", originalKey);
            }
        }
        
        [Fact]
        public async Task AuthenticateAsync_WithoutApiKey_ReturnsFalse()
        {
            // Arrange
            var originalKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
            Environment.SetEnvironmentVariable("OPENROUTER_API_KEY", null);
            
            // Clean up any existing config file to ensure clean state
            var configPath = GetOpenRouterConfigPath();
            var configExisted = File.Exists(configPath);
            if (configExisted)
            {
                File.Delete(configPath);
            }
            
            try
            {
                var provider = new OpenRouterProvider();
                
                // Act
                var result = await provider.AuthenticateAsync();
                
                // Assert
                result.Should().BeFalse();
            }
            finally
            {
                Environment.SetEnvironmentVariable("OPENROUTER_API_KEY", originalKey);
                
                // Clean up any config file created during the test
                if (File.Exists(configPath))
                {
                    File.Delete(configPath);
                }
            }
        }
        
        private static string GetOpenRouterConfigPath()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var saturnDir = Path.Combine(appData, "Saturn", "providers");
            return Path.Combine(saturnDir, "openrouter.config");
        }
        
        [Fact]
        public async Task AuthenticateAsync_WithApiKey_ReturnsTrue()
        {
            // Arrange
            var originalKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
            Environment.SetEnvironmentVariable("OPENROUTER_API_KEY", "test_key");
            
            try
            {
                var provider = new OpenRouterProvider();
                
                // Act
                var result = await provider.AuthenticateAsync();
                
                // Assert
                result.Should().BeTrue();
            }
            finally
            {
                Environment.SetEnvironmentVariable("OPENROUTER_API_KEY", originalKey);
            }
        }
        
        [Fact]
        public async Task GetClientAsync_WithAuthentication_ReturnsClient()
        {
            // Arrange
            var originalKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
            Environment.SetEnvironmentVariable("OPENROUTER_API_KEY", "test_key");
            
            try
            {
                var provider = new OpenRouterProvider();
                
                // Act
                var client = await provider.GetClientAsync();
                
                // Assert
                client.Should().NotBeNull();
                client.ProviderName.Should().Be("OpenRouter");
            }
            finally
            {
                Environment.SetEnvironmentVariable("OPENROUTER_API_KEY", originalKey);
            }
        }
        
        [Fact]
        public async Task GetClientAsync_WithoutAuthentication_ThrowsInvalidOperationException()
        {
            // Arrange
            var originalKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
            Environment.SetEnvironmentVariable("OPENROUTER_API_KEY", null);
            
            try
            {
                var provider = new OpenRouterProvider();
                
                // Act & Assert
                var act = async () => await provider.GetClientAsync();
                await act.Should().ThrowAsync<InvalidOperationException>();
            }
            finally
            {
                Environment.SetEnvironmentVariable("OPENROUTER_API_KEY", originalKey);
            }
        }
        
        [Fact]
        public async Task LogoutAsync_ClearsAuthentication()
        {
            // Arrange
            var originalKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
            Environment.SetEnvironmentVariable("OPENROUTER_API_KEY", "test_key");
            
            try
            {
                var provider = new OpenRouterProvider();
                provider.IsAuthenticated.Should().BeTrue();
                
                // Act
                await provider.LogoutAsync();
                
                // Assert
                provider.IsAuthenticated.Should().BeFalse();
            }
            finally
            {
                Environment.SetEnvironmentVariable("OPENROUTER_API_KEY", originalKey);
            }
        }
        
        [Fact]
        public async Task ConfigurationMethods_DoNotThrow()
        {
            // Arrange
            var provider = new OpenRouterProvider();
            
            // Act & Assert
            await provider.LoadConfigurationAsync();
            await provider.SaveConfigurationAsync();
            
            // These methods should complete without throwing
            true.Should().BeTrue();
        }
        
        [Fact]
        public async Task MultipleClients_AreSeparateInstances()
        {
            // Arrange
            var originalKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
            Environment.SetEnvironmentVariable("OPENROUTER_API_KEY", "test_key");
            
            try
            {
                var provider = new OpenRouterProvider();
                
                // Act
                var client1 = await provider.GetClientAsync();
                var client2 = await provider.GetClientAsync();
                
                // Assert
                client1.Should().NotBeSameAs(client2);
                client1.ProviderName.Should().Be(client2.ProviderName);
            }
            finally
            {
                Environment.SetEnvironmentVariable("OPENROUTER_API_KEY", originalKey);
            }
        }
    }
}