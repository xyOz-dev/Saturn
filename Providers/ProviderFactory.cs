using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Saturn.Providers.OpenRouter;
using Saturn.Providers.Anthropic;

namespace Saturn.Providers
{
    public class ProviderFactory
    {
        private static readonly Dictionary<string, Func<ILLMProvider>> _providers = new();
        
        static ProviderFactory()
        {
            // Register default providers
            RegisterProvider("openrouter", () => new OpenRouterProvider());
            RegisterProvider("anthropic", () => new AnthropicProvider());
        }
        
        public static void RegisterProvider(string name, Func<ILLMProvider> factory)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("Provider name cannot be null or empty", nameof(name));
            
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Provider name cannot be whitespace only", nameof(name));
            
            if (factory == null)
                throw new ArgumentNullException(nameof(factory));
            
            // Validate provider name format (alphanumeric and hyphens only)
            if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^[a-zA-Z0-9\-_]+$"))
                throw new ArgumentException("Provider name must contain only alphanumeric characters, hyphens, and underscores", nameof(name));
            
            // Test factory function to ensure it's valid
            try
            {
                var testProvider = factory();
                if (testProvider == null)
                    throw new ArgumentException("Provider factory function must return a valid ILLMProvider instance", nameof(factory));
            }
            catch (Exception ex) when (!(ex is ArgumentException))
            {
                throw new ArgumentException($"Provider factory function failed during validation: {ex.Message}", nameof(factory), ex);
            }
                
            _providers[name.ToLower()] = factory;
        }
        
        public static ILLMProvider CreateProvider(string name)
        {
            if (string.IsNullOrEmpty(name) || string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Provider name cannot be null, empty, or whitespace", nameof(name));
                
            if (_providers.TryGetValue(name.ToLower(), out var factory))
            {
                return factory();
            }
            
            throw new NotSupportedException($"Provider '{name}' is not supported");
        }
        
        public static List<string> GetAvailableProviders()
        {
            return new List<string>(_providers.Keys);
        }
        
        public static async Task<ILLMProvider> CreateAndAuthenticateAsync(string name)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("Provider name cannot be null or empty", nameof(name));
            
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Provider name cannot be whitespace only", nameof(name));
            
            try
            {
                var provider = CreateProvider(name);
                
                if (provider == null)
                    throw new InvalidOperationException($"Provider factory returned null for '{name}'");
                
                // Load saved configuration
                await provider.LoadConfigurationAsync();
                
                // Authenticate if needed
                if (!provider.IsAuthenticated)
                {
                    var success = await provider.AuthenticateAsync();
                    if (!success)
                    {
                        throw new InvalidOperationException($"Authentication failed for provider '{name}'. Please check your credentials and try again.");
                    }
                }
                
                // Verify provider is ready after authentication
                if (!provider.IsAuthenticated)
                {
                    throw new InvalidOperationException($"Provider '{name}' is not authenticated after successful authentication call. This indicates a provider implementation issue.");
                }
                
                return provider;
            }
            catch (Exception ex) when (!(ex is NotSupportedException || ex is ArgumentException))
            {
                throw new InvalidOperationException($"Failed to initialize provider '{name}': {ex.Message}", ex);
            }
        }
    }
}