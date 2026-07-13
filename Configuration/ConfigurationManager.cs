using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Saturn.Agents.Core;
using Saturn.Configuration.Objects;
using Saturn.Providers;

namespace Saturn.Configuration
{
    public class ConfigurationManager
    {
        private static string AppDataPath
        {
            get
            {
                var overrideDir = Environment.GetEnvironmentVariable("SATURN_CONFIG_DIR");
                if (!string.IsNullOrWhiteSpace(overrideDir))
                {
                    return overrideDir;
                }

                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Saturn");
            }
        }

        private static string ConfigFilePath => Path.Combine(AppDataPath, "agent-config.json");

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() }
        };

        private static readonly SemaphoreSlim SaveLock = new(1, 1);

        public static async Task<PersistedAgentConfiguration?> LoadConfigurationAsync()
        {
            try
            {
                if (!File.Exists(ConfigFilePath))
                {
                    return null;
                }

                var json = await File.ReadAllTextAsync(ConfigFilePath);
                var config = JsonSerializer.Deserialize<PersistedAgentConfiguration>(json, JsonOptions);

                if (config != null && config.RequireCommandApproval == null)
                {
                    config.RequireCommandApproval = true;
                }

                if (config != null && config.EnableUserRules == null)
                {
                    config.EnableUserRules = true;
                }

                if (config != null)
                {
                    MigrateLegacyProviderFields(config);
                }

                return config;
            }
            catch (Exception ex)
            {

                return null;
            }
        }

        private static void MigrateLegacyProviderFields(PersistedAgentConfiguration config)
        {
            config.ActiveProvider ??= "openrouter";

            var rebuilt = new Dictionary<string, PersistedProviderConfiguration>(StringComparer.OrdinalIgnoreCase);
            if (config.Providers != null)
            {
                foreach (var kvp in config.Providers)
                {
                    if (!rebuilt.ContainsKey(kvp.Key))
                    {
                        rebuilt[kvp.Key] = kvp.Value;
                    }
                }
            }
            config.Providers = rebuilt;

            if (!config.Providers.TryGetValue(config.ActiveProvider, out var providerConfig))
            {
                providerConfig = new PersistedProviderConfiguration();
                config.Providers[config.ActiveProvider] = providerConfig;
            }

            providerConfig.Model ??= config.Model;
        }

        public static async Task SaveConfigurationAsync(PersistedAgentConfiguration config)
        {
            await SaveLock.WaitAsync();
            try
            {
                await SaveConfigurationLockedAsync(config);
            }
            finally
            {
                SaveLock.Release();
            }
        }

        private static async Task SaveConfigurationLockedAsync(PersistedAgentConfiguration config)
        {
            try
            {
                if (config.ActiveProvider == null || config.Providers == null)
                {
                    var existing = await LoadConfigurationAsync();
                    config.ActiveProvider ??= existing?.ActiveProvider;
                    config.Providers ??= existing?.Providers;
                }

                if (!string.IsNullOrWhiteSpace(config.ActiveProvider) && !string.IsNullOrWhiteSpace(config.Model))
                {
                    config.Providers ??= new Dictionary<string, PersistedProviderConfiguration>(StringComparer.OrdinalIgnoreCase);
                    if (!config.Providers.TryGetValue(config.ActiveProvider, out var providerConfig))
                    {
                        providerConfig = new PersistedProviderConfiguration();
                        config.Providers[config.ActiveProvider] = providerConfig;
                    }
                    providerConfig.Model = config.Model;
                }

                if (!Directory.Exists(AppDataPath))
                {
                    Directory.CreateDirectory(AppDataPath);
                }

                var json = JsonSerializer.Serialize(WithProtectedSecrets(config), JsonOptions);
                var tempPath = ConfigFilePath + ".tmp";
                await File.WriteAllTextAsync(tempPath, json);
                File.Move(tempPath, ConfigFilePath, overwrite: true);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to save configuration: {ex.Message}");
            }
        }

        public static async Task SaveProviderSelectionAsync(string providerName, ProviderSettings settings, string? model = null)
        {
            await SaveLock.WaitAsync();
            try
            {
                await SaveProviderSelectionLockedAsync(providerName, settings, model);
            }
            finally
            {
                SaveLock.Release();
            }
        }

        private static async Task SaveProviderSelectionLockedAsync(string providerName, ProviderSettings settings, string? model)
        {
            var config = await LoadConfigurationAsync() ?? new PersistedAgentConfiguration();

            config.ActiveProvider = providerName;
            config.Providers ??= new Dictionary<string, PersistedProviderConfiguration>(StringComparer.OrdinalIgnoreCase);

            if (!config.Providers.TryGetValue(providerName, out var providerConfig))
            {
                providerConfig = new PersistedProviderConfiguration();
                config.Providers[providerName] = providerConfig;
            }

            var toPersist = new Dictionary<string, string?>(settings.Values, StringComparer.OrdinalIgnoreCase);
            if (ProviderRegistry.TryGet(providerName, out var provider))
            {
                foreach (var descriptor in provider.SettingDescriptors)
                {
                    if (descriptor.Kind != ProviderSettingKind.Secret ||
                        string.IsNullOrWhiteSpace(descriptor.EnvironmentVariable))
                    {
                        continue;
                    }

                    var envValue = Environment.GetEnvironmentVariable(descriptor.EnvironmentVariable);
                    if (toPersist.TryGetValue(descriptor.Key, out var saved) &&
                        !string.IsNullOrEmpty(saved) &&
                        string.Equals(saved, envValue, StringComparison.Ordinal))
                    {
                        toPersist.Remove(descriptor.Key);
                    }
                }
            }
            providerConfig.Settings = toPersist;
            if (!string.IsNullOrWhiteSpace(model))
            {
                providerConfig.Model = model;
                config.Model = model;
            }

            await SaveConfigurationLockedAsync(config);
        }

        public static ProviderSettings GetProviderSettings(PersistedAgentConfiguration? config, string providerName)
        {
            var settings = new ProviderSettings();
            if (config?.Providers != null &&
                config.Providers.TryGetValue(providerName, out var providerConfig) &&
                providerConfig.Settings != null)
            {
                foreach (var kvp in providerConfig.Settings)
                {
                    settings.Values[kvp.Key] = SecretProtector.Unprotect(kvp.Value, AppDataPath);
                }
            }
            return settings;
        }

        /// <summary>
        /// Returns a copy of the configuration with secret-kind provider settings encrypted,
        /// leaving the caller's in-memory configuration in plaintext.
        /// </summary>
        private static PersistedAgentConfiguration WithProtectedSecrets(PersistedAgentConfiguration config)
        {
            if (config.Providers == null)
            {
                return config;
            }

            Dictionary<string, PersistedProviderConfiguration>? protectedProviders = null;

            foreach (var (providerName, providerConfig) in config.Providers)
            {
                var settings = providerConfig.Settings;
                if (settings == null || !ProviderRegistry.TryGet(providerName, out var provider))
                {
                    continue;
                }

                Dictionary<string, string?>? protectedSettings = null;
                foreach (var descriptor in provider.SettingDescriptors)
                {
                    if (descriptor.Kind != ProviderSettingKind.Secret)
                    {
                        continue;
                    }

                    if (settings.TryGetValue(descriptor.Key, out var value) &&
                        !string.IsNullOrEmpty(value) &&
                        !SecretProtector.IsProtected(value))
                    {
                        protectedSettings ??= new Dictionary<string, string?>(settings, StringComparer.OrdinalIgnoreCase);
                        protectedSettings[descriptor.Key] = SecretProtector.Protect(value, AppDataPath);
                    }
                }

                if (protectedSettings != null)
                {
                    protectedProviders ??= new Dictionary<string, PersistedProviderConfiguration>(config.Providers, StringComparer.OrdinalIgnoreCase);
                    protectedProviders[providerName] = new PersistedProviderConfiguration
                    {
                        Settings = protectedSettings,
                        Model = providerConfig.Model
                    };
                }
            }

            if (protectedProviders == null)
            {
                return config;
            }

            return new PersistedAgentConfiguration
            {
                Name = config.Name,
                Model = config.Model,
                Temperature = config.Temperature,
                MaxTokens = config.MaxTokens,
                TopP = config.TopP,
                EnableStreaming = config.EnableStreaming,
                MaintainHistory = config.MaintainHistory,
                MaxHistoryMessages = config.MaxHistoryMessages,
                EnableTools = config.EnableTools,
                ToolNames = config.ToolNames,
                RequireCommandApproval = config.RequireCommandApproval,
                EnableUserRules = config.EnableUserRules,
                ActiveProvider = config.ActiveProvider,
                Providers = protectedProviders
            };
        }

        public static string? GetProviderModel(PersistedAgentConfiguration? config, string providerName)
        {
            if (config?.Providers != null &&
                config.Providers.TryGetValue(providerName, out var providerConfig) &&
                !string.IsNullOrWhiteSpace(providerConfig.Model))
            {
                return providerConfig.Model;
            }

            if (string.Equals(config?.ActiveProvider, providerName, StringComparison.OrdinalIgnoreCase))
            {
                return config?.Model;
            }

            return null;
        }

        public static PersistedAgentConfiguration FromAgentConfiguration(AgentConfiguration agentConfig)
        {
            return new PersistedAgentConfiguration
            {
                Name = agentConfig.Name,
                Model = agentConfig.Model,
                Temperature = agentConfig.Temperature,
                MaxTokens = agentConfig.MaxTokens,
                TopP = agentConfig.TopP,
                EnableStreaming = agentConfig.EnableStreaming,
                MaintainHistory = agentConfig.MaintainHistory,
                MaxHistoryMessages = agentConfig.MaxHistoryMessages,
                EnableTools = agentConfig.EnableTools,
                ToolNames = agentConfig.ToolNames,
                RequireCommandApproval = agentConfig.RequireCommandApproval,
                EnableUserRules = agentConfig.EnableUserRules
            };
        }

        public static void ApplyToAgentConfiguration(AgentConfiguration target, PersistedAgentConfiguration source)
        {
            target.Name = source.Name ?? target.Name;
            target.Model = source.Model ?? target.Model;
            target.Temperature = source.Temperature ?? target.Temperature;
            target.MaxTokens = source.MaxTokens ?? target.MaxTokens;
            target.TopP = source.TopP ?? target.TopP;
            target.EnableStreaming = source.EnableStreaming;
            target.MaintainHistory = source.MaintainHistory;
            target.MaxHistoryMessages = source.MaxHistoryMessages ?? target.MaxHistoryMessages;
            target.EnableTools = source.EnableTools;
            target.ToolNames = source.ToolNames ?? target.ToolNames;
            target.RequireCommandApproval = source.RequireCommandApproval ?? true;
            target.EnableUserRules = source.EnableUserRules ?? true;
        }
    }
}
