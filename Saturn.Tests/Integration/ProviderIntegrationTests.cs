using Xunit;
using FluentAssertions;
using Saturn.Providers;
using Saturn.Tests.Mocks;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using Saturn.Providers.Models;

namespace Saturn.Tests.Integration
{
    public class ProviderIntegrationTests
    {
        [Fact]
        public async Task ProviderFactory_CreateOpenRouterProvider_Success()
        {
            // Arrange - Set up environment for OpenRouter
            var originalKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
            Environment.SetEnvironmentVariable("OPENROUTER_API_KEY", "test_key");
            
            try
            {
                // Act
                var provider = ProviderFactory.CreateProvider("openrouter");
                
                // Assert
                provider.Should().NotBeNull();
                provider.Name.Should().Be("OpenRouter");
                provider.AuthType.Should().Be(AuthenticationType.ApiKey);
            }
            finally
            {
                // Cleanup
                Environment.SetEnvironmentVariable("OPENROUTER_API_KEY", originalKey);
            }
        }
        
        [Fact]
        public async Task ProviderFactory_CreateAnthropicProvider_Success()
        {
            // Act
            var provider = ProviderFactory.CreateProvider("anthropic");
            
            // Assert
            provider.Should().NotBeNull();
            provider.Name.Should().Be("Anthropic");
            provider.AuthType.Should().Be(AuthenticationType.OAuth);
        }
        
        [Fact]
        public async Task Provider_Lifecycle_LoadConfigSaveConfig()
        {
            // Arrange
            var provider = ProviderFactory.CreateProvider("anthropic");
            
            // Act & Assert - Should not throw
            await provider.LoadConfigurationAsync();
            await provider.SaveConfigurationAsync();
            
            // Verify basic functionality works
            provider.Should().NotBeNull();
        }
        
        [Fact]
        public async Task Provider_LogoutFlow_ClearsAuthentication()
        {
            // Arrange
            var mockProvider = new MockLLMProvider { IsAuthenticated = true };
            
            // Act
            await mockProvider.LogoutAsync();
            
            // Assert
            mockProvider.IsAuthenticated.Should().BeFalse();
            mockProvider.LogoutCalled.Should().BeTrue();
        }
        
        [Fact]
        public async Task Provider_GetClient_WithoutAuthentication_ThrowsOrAuthenticates()
        {
            // Arrange
            var mockProvider = new MockLLMProvider { IsAuthenticated = false };
            
            // Act
            var client = await mockProvider.GetClientAsync();
            
            // Assert
            client.Should().NotBeNull();
            mockProvider.GetClientCalled.Should().BeTrue();
        }
        
        [Fact]
        public async Task Client_BasicFunctionality_Works()
        {
            // Arrange
            var mockClient = new MockLLMClient();
            var request = new ChatRequest
            {
                Messages = new List<ChatMessage>
                {
                    new() { Role = "user", Content = "Test message" }
                },
                Model = "test-model"
            };
            
            // Act
            var response = await mockClient.ChatCompletionAsync(request);
            
            // Assert
            response.Should().NotBeNull();
            response.Message.Should().NotBeNull();
            response.Message.Role.Should().Be("assistant");
            mockClient.ReceivedRequests.Should().HaveCount(1);
            mockClient.ReceivedRequests[0].Should().BeSameAs(request);
        }
        
        [Fact]
        public async Task Client_StreamingFunctionality_Works()
        {
            // Arrange
            var mockClient = new MockLLMClient();
            var request = new ChatRequest
            {
                Messages = new List<ChatMessage>
                {
                    new() { Role = "user", Content = "Stream test" }
                },
                Model = "test-model"
            };
            
            var chunks = new List<StreamChunk>();
            
            // Act
            var response = await mockClient.StreamChatAsync(request, chunk =>
            {
                chunks.Add(chunk);
                return Task.CompletedTask;
            });
            
            // Assert
            response.Should().NotBeNull();
            chunks.Should().NotBeEmpty();
            chunks.Should().Contain(c => c.IsComplete);
            mockClient.ReceivedRequests.Should().HaveCount(1);
        }
        
        [Fact]
        public async Task Client_GetModels_ReturnsModels()
        {
            // Arrange
            var mockClient = new MockLLMClient();
            
            // Act
            var models = await mockClient.GetModelsAsync();
            
            // Assert
            models.Should().NotBeNull();
            models.Should().NotBeEmpty();
            models.Should().OnlyContain(m => !string.IsNullOrEmpty(m.Id));
            models.Should().OnlyContain(m => !string.IsNullOrEmpty(m.Name));
        }
        
        [Fact]
        public void ProviderFactory_SupportsProviderRegistration()
        {
            // Arrange
            var testProviderName = "test-integration-provider";
            var mockProvider = new MockLLMProvider { Name = "Test Integration Provider" };
            
            // Act
            ProviderFactory.RegisterProvider(testProviderName, () => mockProvider);
            var availableProviders = ProviderFactory.GetAvailableProviders();
            
            // Assert
            availableProviders.Should().Contain(testProviderName);
            
            var createdProvider = ProviderFactory.CreateProvider(testProviderName);
            createdProvider.Should().NotBeNull();
            createdProvider.Name.Should().Be("Test Integration Provider");
        }
        
        [Fact]
        public async Task EndToEnd_ProviderCreationToClient_Works()
        {
            // Arrange
            var testProviderName = "e2e-test-provider";
            var mockProvider = new MockLLMProvider
            {
                Name = "E2E Test Provider",
                IsAuthenticated = true,
                ClientToReturn = new MockLLMClient()
            };
            
            ProviderFactory.RegisterProvider(testProviderName, () => mockProvider);
            
            // Act
            var provider = ProviderFactory.CreateProvider(testProviderName);
            await provider.LoadConfigurationAsync();
            
            if (!provider.IsAuthenticated)
            {
                await provider.AuthenticateAsync();
            }
            
            var client = await provider.GetClientAsync();
            var models = await client.GetModelsAsync();
            
            var chatRequest = new ChatRequest
            {
                Messages = new List<ChatMessage>
                {
                    new() { Role = "user", Content = "Integration test message" }
                },
                Model = "test-model"
            };
            
            var chatResponse = await client.ChatCompletionAsync(chatRequest);
            
            // Assert
            provider.Should().NotBeNull();
            provider.IsAuthenticated.Should().BeTrue();
            client.Should().NotBeNull();
            client.IsReady.Should().BeTrue();
            models.Should().NotBeEmpty();
            chatResponse.Should().NotBeNull();
            chatResponse.Message.Should().NotBeNull();
            
            // Verify the flow worked
            mockProvider.LoadConfigurationCalled.Should().BeTrue();
            mockProvider.GetClientCalled.Should().BeTrue();
        }
        
        [Fact]
        public async Task ProviderSwitching_Scenario_Works()
        {
            // Arrange
            var provider1Name = "provider1";
            var provider2Name = "provider2";
            
            var provider1 = new MockLLMProvider 
            { 
                Name = "Provider 1",
                ClientToReturn = new MockLLMClient { ProviderName = "Provider1" }
            };
            
            var provider2 = new MockLLMProvider 
            { 
                Name = "Provider 2", 
                ClientToReturn = new MockLLMClient { ProviderName = "Provider2" }
            };
            
            ProviderFactory.RegisterProvider(provider1Name, () => provider1);
            ProviderFactory.RegisterProvider(provider2Name, () => provider2);
            
            // Act - Simulate switching between providers
            var currentProvider = ProviderFactory.CreateProvider(provider1Name);
            var client1 = await currentProvider.GetClientAsync();
            
            // Switch to second provider
            await currentProvider.LogoutAsync();
            currentProvider = ProviderFactory.CreateProvider(provider2Name);
            var client2 = await currentProvider.GetClientAsync();
            
            // Assert
            client1.ProviderName.Should().Be("Provider1");
            client2.ProviderName.Should().Be("Provider2");
            provider1.LogoutCalled.Should().BeTrue();
        }
        
        [Fact]
        public async Task ErrorHandling_ProviderFailures_HandledGracefully()
        {
            // Arrange
            var faultyProvider = new MockLLMProvider
            {
                ExceptionToThrow = new InvalidOperationException("Simulated provider failure")
            };
            
            var faultyProviderName = "faulty-provider";
            ProviderFactory.RegisterProvider(faultyProviderName, () => faultyProvider);
            
            // Act & Assert - Authentication failure
            var provider = ProviderFactory.CreateProvider(faultyProviderName);
            var act1 = async () => await provider.AuthenticateAsync();
            await act1.Should().ThrowAsync<InvalidOperationException>();
            
            // Act & Assert - GetClient failure
            faultyProvider.ExceptionToThrow = new InvalidOperationException("Client creation failed");
            var act2 = async () => await provider.GetClientAsync();
            await act2.Should().ThrowAsync<InvalidOperationException>();
        }
    }
}