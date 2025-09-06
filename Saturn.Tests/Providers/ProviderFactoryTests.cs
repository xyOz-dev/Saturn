using Xunit;
using FluentAssertions;
using Saturn.Providers;
using Saturn.Tests.Mocks;
using System;
using System.Threading.Tasks;
using System.Linq;

namespace Saturn.Tests.Providers
{
    public class ProviderFactoryTests
    {
        [Fact]
        public void GetAvailableProviders_ReturnsDefaultProviders()
        {
            // Act
            var providers = ProviderFactory.GetAvailableProviders();
            
            // Assert
            providers.Should().NotBeEmpty();
            providers.Should().Contain("openrouter");
            providers.Should().Contain("anthropic");
        }
        
        [Fact]
        public void CreateProvider_WithValidName_ReturnsProvider()
        {
            // Act
            var provider = ProviderFactory.CreateProvider("openrouter");
            
            // Assert
            provider.Should().NotBeNull();
            provider.Name.Should().Be("OpenRouter");
        }
        
        [Fact]
        public void CreateProvider_WithAnthropicName_ReturnsAnthropicProvider()
        {
            // Act
            var provider = ProviderFactory.CreateProvider("anthropic");
            
            // Assert
            provider.Should().NotBeNull();
            provider.Name.Should().Be("Anthropic");
        }
        
        [Fact]
        public void CreateProvider_WithInvalidName_ThrowsNotSupportedException()
        {
            // Act & Assert
            var act = () => ProviderFactory.CreateProvider("invalid_provider");
            act.Should().Throw<NotSupportedException>()
                .WithMessage("*invalid_provider*");
        }
        
        [Fact]
        public void CreateProvider_WithNullName_ThrowsArgumentException()
        {
            // Act & Assert
            var act = () => ProviderFactory.CreateProvider(null);
            act.Should().Throw<ArgumentException>();
        }
        
        [Fact]
        public void CreateProvider_WithEmptyName_ThrowsArgumentException()
        {
            // Act & Assert
            var act = () => ProviderFactory.CreateProvider("");
            act.Should().Throw<ArgumentException>();
        }
        
        [Fact]
        public void CreateProvider_WithWhitespaceName_ThrowsArgumentException()
        {
            // Act & Assert
            var act = () => ProviderFactory.CreateProvider("   ");
            act.Should().Throw<ArgumentException>();
        }
        
        [Fact]
        public void CreateProvider_IsCaseInsensitive()
        {
            // Act
            var provider1 = ProviderFactory.CreateProvider("OPENROUTER");
            var provider2 = ProviderFactory.CreateProvider("OpenRouter");
            var provider3 = ProviderFactory.CreateProvider("openrouter");
            
            // Assert
            provider1.Should().NotBeNull();
            provider2.Should().NotBeNull();
            provider3.Should().NotBeNull();
            provider1.Name.Should().Be(provider2.Name);
            provider2.Name.Should().Be(provider3.Name);
        }
        
        [Fact]
        public void RegisterProvider_WithValidData_AddsProvider()
        {
            // Arrange
            var providerName = "test_provider";
            var mockProvider = new MockLLMProvider { Name = "Test Provider" };
            
            try
            {
                // Act
                ProviderFactory.RegisterProvider(providerName, () => mockProvider);
                
                // Assert
                var providers = ProviderFactory.GetAvailableProviders();
                providers.Should().Contain(providerName);
                
                var createdProvider = ProviderFactory.CreateProvider(providerName);
                createdProvider.Should().NotBeNull();
                createdProvider.Name.Should().Be("Test Provider");
            }
            finally
            {
                // Cleanup - remove test provider
                // Note: The actual implementation would need an UnregisterProvider method
                // For now, we rely on the fact that each test gets a fresh static state
            }
        }
        
        [Fact]
        public void RegisterProvider_WithNullName_ThrowsArgumentException()
        {
            // Act & Assert
            var act = () => ProviderFactory.RegisterProvider(null, () => new MockLLMProvider());
            act.Should().Throw<ArgumentException>();
        }
        
        [Fact]
        public void RegisterProvider_WithEmptyName_ThrowsArgumentException()
        {
            // Act & Assert
            var act = () => ProviderFactory.RegisterProvider("", () => new MockLLMProvider());
            act.Should().Throw<ArgumentException>();
        }
        
        [Fact]
        public void RegisterProvider_WithNullFactory_ThrowsArgumentNullException()
        {
            // Act & Assert
            var act = () => ProviderFactory.RegisterProvider("test", null);
            act.Should().Throw<ArgumentNullException>();
        }
        
        [Fact]
        public void RegisterProvider_WithExistingName_OverwritesProvider()
        {
            // Arrange
            var providerName = "test_overwrite";
            var firstProvider = new MockLLMProvider { Name = "First Provider" };
            var secondProvider = new MockLLMProvider { Name = "Second Provider" };
            
            // Act
            ProviderFactory.RegisterProvider(providerName, () => firstProvider);
            ProviderFactory.RegisterProvider(providerName, () => secondProvider);
            
            // Assert
            var createdProvider = ProviderFactory.CreateProvider(providerName);
            createdProvider.Name.Should().Be("Second Provider");
        }
        
        [Fact]
        public async Task CreateAndAuthenticateAsync_WithValidProvider_ReturnsAuthenticatedProvider()
        {
            // Arrange
            var mockProvider = new MockLLMProvider();
            var providerName = "test_auth_provider";
            ProviderFactory.RegisterProvider(providerName, () => mockProvider);
            
            // Act
            var provider = await ProviderFactory.CreateAndAuthenticateAsync(providerName);
            
            // Assert
            provider.Should().NotBeNull();
            provider.IsAuthenticated.Should().BeTrue();
            mockProvider.LoadConfigurationCalled.Should().BeTrue();
        }
        
        [Fact]
        public async Task CreateAndAuthenticateAsync_WithUnauthenticatedProvider_AttemptsAuthentication()
        {
            // Arrange
            var mockProvider = new MockLLMProvider 
            { 
                IsAuthenticated = false,
                AuthenticateResult = true 
            };
            var providerName = "test_unauth_provider";
            ProviderFactory.RegisterProvider(providerName, () => mockProvider);
            
            // Act
            var provider = await ProviderFactory.CreateAndAuthenticateAsync(providerName);
            
            // Assert
            provider.Should().NotBeNull();
            provider.IsAuthenticated.Should().BeTrue();
            mockProvider.AuthenticateCalled.Should().BeTrue();
        }
        
        [Fact]
        public async Task CreateAndAuthenticateAsync_WithFailedAuthentication_ThrowsInvalidOperationException()
        {
            // Arrange
            var mockProvider = new MockLLMProvider 
            { 
                IsAuthenticated = false,
                AuthenticateResult = false 
            };
            var providerName = "test_failed_auth";
            ProviderFactory.RegisterProvider(providerName, () => mockProvider);
            
            // Act & Assert
            var act = async () => await ProviderFactory.CreateAndAuthenticateAsync(providerName);
            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*Authentication failed for provider*");
        }
        
        [Fact]
        public async Task CreateAndAuthenticateAsync_WithInvalidProvider_ThrowsNotSupportedException()
        {
            // Act & Assert
            var act = async () => await ProviderFactory.CreateAndAuthenticateAsync("nonexistent_provider");
            await act.Should().ThrowAsync<NotSupportedException>();
        }
        
        [Fact]
        public async Task CreateAndAuthenticateAsync_WithAuthenticationException_ThrowsInvalidOperationException()
        {
            // Arrange
            var mockProvider = new MockLLMProvider 
            { 
                IsAuthenticated = false,
                ExceptionToThrow = new Exception("Authentication failed")
            };
            var providerName = "test_auth_exception";
            ProviderFactory.RegisterProvider(providerName, () => mockProvider);
            
            // Act & Assert
            var act = async () => await ProviderFactory.CreateAndAuthenticateAsync(providerName);
            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*Failed to initialize provider*");
        }
        
        [Fact]
        public void CreateProvider_MultipleCallsWithSameName_ReturnsDifferentInstances()
        {
            // Act
            var provider1 = ProviderFactory.CreateProvider("openrouter");
            var provider2 = ProviderFactory.CreateProvider("openrouter");
            
            // Assert
            provider1.Should().NotBeNull();
            provider2.Should().NotBeNull();
            provider1.Should().NotBeSameAs(provider2, "each call should return a new instance");
        }
        
        [Fact]
        public void GetAvailableProviders_ReturnsListCopy()
        {
            // Act
            var providers1 = ProviderFactory.GetAvailableProviders();
            var providers2 = ProviderFactory.GetAvailableProviders();
            
            // Assert
            providers1.Should().NotBeSameAs(providers2, "should return a copy, not the same list");
            providers1.Should().BeEquivalentTo(providers2, "content should be the same");
        }
    }
}