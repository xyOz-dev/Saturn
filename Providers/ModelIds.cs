using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace Saturn.Providers
{
    /// <summary>
    /// Centralized model ID resolver that maps provider-agnostic identifiers to provider-specific model IDs
    /// Configuration is loaded from model-mappings.json
    /// </summary>
    public static class ModelIds
    {
        private static readonly Lazy<ModelMappingConfiguration> _configuration = new(LoadConfiguration);
        
        private class ModelMappingConfiguration
        {
            public Dictionary<string, ProviderConfiguration> Providers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        }
        
        private class ProviderConfiguration
        {
            public Dictionary<string, string> Models { get; set; } = new(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, PricingInfo>? Pricing { get; set; }
        }
        
        private class PricingInfo
        {
            public double InputCost { get; set; }
            public double OutputCost { get; set; }
        }
        
        private static ModelMappingConfiguration LoadConfiguration()
        {
            try
            {
                // Try multiple locations for the JSON file
                string? jsonPath = null;
                
                // 1. First try next to the executing assembly
                var assemblyLocation = Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrEmpty(assemblyLocation))
                {
                    var assemblyDir = Path.GetDirectoryName(assemblyLocation);
                    if (assemblyDir != null)
                    {
                        var path = Path.Combine(assemblyDir, "model-mappings.json");
                        if (File.Exists(path))
                        {
                            jsonPath = path;
                        }
                    }
                }
                
                // 2. Try in the Providers directory relative to current directory
                if (jsonPath == null)
                {
                    var providersPath = Path.Combine("Providers", "model-mappings.json");
                    if (File.Exists(providersPath))
                    {
                        jsonPath = providersPath;
                    }
                }
                
                // 3. Try in the current directory
                if (jsonPath == null)
                {
                    if (File.Exists("model-mappings.json"))
                    {
                        jsonPath = "model-mappings.json";
                    }
                }
                
                // 4. If no JSON file found, throw exception
                if (jsonPath == null || !File.Exists(jsonPath))
                {
                    throw new InvalidOperationException(
                        "model-mappings.json not found. Expected locations: " +
                        "next to executable, in Providers directory, or current directory.");
                }
                
                var json = File.ReadAllText(jsonPath);
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };
                
                var config = JsonSerializer.Deserialize<ModelMappingConfiguration>(json, options);
                if (config == null || config.Providers == null || config.Providers.Count == 0)
                {
                    throw new InvalidOperationException("model-mappings.json is empty or invalid.");
                }
                
                // Convert to case-insensitive dictionaries
                var result = new ModelMappingConfiguration
                {
                    Providers = new Dictionary<string, ProviderConfiguration>(StringComparer.OrdinalIgnoreCase)
                };
                
                foreach (var provider in config.Providers)
                {
                    var providerConfig = new ProviderConfiguration
                    {
                        Models = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    };
                    
                    if (provider.Value?.Models != null)
                    {
                        foreach (var model in provider.Value.Models)
                        {
                            providerConfig.Models[model.Key] = model.Value;
                        }
                    }
                    
                    if (provider.Value?.Pricing != null)
                    {
                        providerConfig.Pricing = provider.Value.Pricing;
                    }
                    
                    result.Providers[provider.Key] = providerConfig;
                }
                
                return result;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to load model-mappings.json: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Resolves a provider-agnostic model identifier to a provider-specific model ID
        /// </summary>
        /// <param name="providerName">The name of the provider (e.g., "OpenRouter", "Anthropic")</param>
        /// <param name="modelIdentifier">The provider-agnostic model identifier (e.g., "default", "sonnet", "opus")</param>
        /// <returns>The provider-specific model ID</returns>
        /// <exception cref="ArgumentException">Thrown when provider is unknown or model cannot be resolved</exception>
        public static string Resolve(string providerName, string? modelIdentifier)
        {
            if (string.IsNullOrWhiteSpace(providerName))
            {
                throw new ArgumentException("Provider name cannot be null or empty", nameof(providerName));
            }

            // If no model identifier provided, use default
            if (string.IsNullOrWhiteSpace(modelIdentifier))
            {
                modelIdentifier = "default";
            }

            var config = _configuration.Value;
            
            // Check if provider exists
            if (!config.Providers.TryGetValue(providerName, out var providerConfig))
            {
                throw new ArgumentException($"Unknown provider: {providerName}. Supported providers: {string.Join(", ", config.Providers.Keys)}", nameof(providerName));
            }

            // First check if the model identifier is already a provider-specific ID (pass-through)
            // This allows backward compatibility with existing configurations
            if (modelIdentifier.Contains("/"))
            {
                // Likely already a provider-specific ID (e.g., "anthropic/claude-sonnet-4"), return as-is
                return modelIdentifier;
            }

            // Try to resolve from mappings
            if (providerConfig.Models.TryGetValue(modelIdentifier, out var resolvedModel))
            {
                return resolvedModel;
            }

            // If not found in mappings, throw clear error
            throw new ArgumentException(
                $"Cannot resolve model '{modelIdentifier}' for provider '{providerName}'. " +
                $"Available models: {string.Join(", ", providerConfig.Models.Keys)}", 
                nameof(modelIdentifier));
        }

        /// <summary>
        /// Tries to resolve a model identifier without throwing exceptions
        /// </summary>
        /// <param name="providerName">The name of the provider</param>
        /// <param name="modelIdentifier">The model identifier to resolve</param>
        /// <param name="resolvedModel">The resolved model ID if successful</param>
        /// <returns>True if resolution was successful, false otherwise</returns>
        public static bool TryResolve(string providerName, string? modelIdentifier, out string? resolvedModel)
        {
            resolvedModel = null;
            
            try
            {
                resolvedModel = Resolve(providerName, modelIdentifier);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Gets the default model for a given provider
        /// </summary>
        /// <param name="providerName">The name of the provider</param>
        /// <returns>The default model ID for the provider</returns>
        public static string GetDefault(string providerName)
        {
            return Resolve(providerName, "default");
        }

        /// <summary>
        /// Checks if a provider is supported
        /// </summary>
        /// <param name="providerName">The name of the provider to check</param>
        /// <returns>True if the provider is supported, false otherwise</returns>
        public static bool IsProviderSupported(string providerName)
        {
            if (string.IsNullOrWhiteSpace(providerName))
                return false;
                
            return _configuration.Value.Providers.ContainsKey(providerName);
        }

        /// <summary>
        /// Gets available model identifiers for a provider
        /// </summary>
        /// <param name="providerName">The name of the provider</param>
        /// <returns>List of available model identifiers</returns>
        public static IEnumerable<string> GetAvailableModels(string providerName)
        {
            if (_configuration.Value.Providers.TryGetValue(providerName, out var providerConfig))
            {
                return providerConfig.Models.Keys;
            }
            return Array.Empty<string>();
        }
        
        /// <summary>
        /// Reloads the configuration from the JSON file
        /// Useful for testing or when the JSON file has been updated
        /// </summary>
        public static void ReloadConfiguration()
        {
            if (_configuration.IsValueCreated)
            {
                // Force reload by creating a new Lazy instance
                var newConfig = LoadConfiguration();
                // This is a bit of a hack but works for testing purposes
                // In production, you might want to use a different approach
                _configuration.Value.Providers = newConfig.Providers;
            }
        }
    }
}