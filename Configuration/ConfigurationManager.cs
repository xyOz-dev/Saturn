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
using Saturn.Tools.Search;

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
                return await LoadFromDiskAsync();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Loads the configuration for use as the base of a save/rewrite operation. Unlike
        /// <see cref="LoadConfigurationAsync"/>, this does not swallow read failures for a file
        /// that exists: a transient read error (locked file, corrupt JSON, etc.) must not be
        /// treated the same as "no config file exists yet", since callers use the result as the
        /// base object they overwrite the config file with. Returning null in that case would
        /// cause the save to silently rebuild a fresh configuration and wipe out any other
        /// providers' settings.
        /// </summary>
        private static async Task<PersistedAgentConfiguration?> LoadForRewriteAsync()
        {
            try
            {
                return await LoadFromDiskAsync();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to load existing configuration for save: {ex.Message}");
                throw new IOException($"Refusing to save configuration: existing config file at '{ConfigFilePath}' could not be read ({ex.Message}).", ex);
            }
        }

        private static async Task<PersistedAgentConfiguration?> LoadFromDiskAsync()
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
                if (config.ActiveProvider == null || config.Providers == null ||
                    config.SearchProvider == null || config.SearchProviders == null)
                {
                    var existing = await LoadForRewriteAsync();
                    config.ActiveProvider ??= existing?.ActiveProvider;
                    config.Providers ??= existing?.Providers;
                    config.SearchProvider ??= existing?.SearchProvider;
                    config.SearchProviders ??= existing?.SearchProviders;
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
            var config = await LoadForRewriteAsync() ?? new PersistedAgentConfiguration();

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
            return ReadProviderSettings(config?.Providers, providerName);
        }

        public static ProviderSettings GetSearchProviderSettings(PersistedAgentConfiguration? config, string providerName)
        {
            return ReadProviderSettings(config?.SearchProviders, providerName);
        }

        private static ProviderSettings ReadProviderSettings(
            Dictionary<string, PersistedProviderConfiguration>? map, string providerName)
        {
            var settings = new ProviderSettings();
            if (map != null &&
                map.TryGetValue(providerName, out var providerConfig) &&
                providerConfig.Settings != null)
            {
                foreach (var kvp in providerConfig.Settings)
                {
                    settings.Values[kvp.Key] = SecretProtector.Unprotect(kvp.Value, AppDataPath);
                }
            }
            return settings;
        }

        public static async Task SaveSearchProviderSelectionAsync(string providerName, ProviderSettings settings)
        {
            await SaveLock.WaitAsync();
            try
            {
                await SaveSearchProviderSelectionLockedAsync(providerName, settings);
            }
            finally
            {
                SaveLock.Release();
            }
        }

        private static async Task SaveSearchProviderSelectionLockedAsync(string providerName, ProviderSettings settings)
        {
            var config = await LoadForRewriteAsync() ?? new PersistedAgentConfiguration();

            config.SearchProvider = providerName;
            config.SearchProviders ??= new Dictionary<string, PersistedProviderConfiguration>(StringComparer.OrdinalIgnoreCase);

            if (!config.SearchProviders.TryGetValue(providerName, out var providerConfig))
            {
                providerConfig = new PersistedProviderConfiguration();
                config.SearchProviders[providerName] = providerConfig;
            }

            var toPersist = new Dictionary<string, string?>(settings.Values, StringComparer.OrdinalIgnoreCase);
            if (SearchProviderRegistry.TryGet(providerName, out var provider))
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

            await SaveConfigurationLockedAsync(config);
        }

        /// <summary>
        /// Returns a copy of the configuration with secret-kind provider settings encrypted,
        /// leaving the caller's in-memory configuration in plaintext.
        /// </summary>
        private static PersistedAgentConfiguration WithProtectedSecrets(PersistedAgentConfiguration config)
        {
            var protectedProviders = ProtectProviderMap(
                config.Providers,
                name => ProviderRegistry.TryGet(name, out var p) ? p.SettingDescriptors : null);

            var protectedSearchProviders = ProtectProviderMap(
                config.SearchProviders,
                name => SearchProviderRegistry.TryGet(name, out var p) ? p.SettingDescriptors : null);

            if (protectedProviders == null && protectedSearchProviders == null)
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
                Providers = protectedProviders ?? config.Providers,
                SearchProvider = config.SearchProvider,
                SearchProviders = protectedSearchProviders ?? config.SearchProviders
            };
        }

        /// <summary>
        /// Returns a copy of <paramref name="map"/> with every secret-kind setting encrypted, or
        /// null if there was nothing to protect (letting the caller keep the original reference).
        /// </summary>
        private static Dictionary<string, PersistedProviderConfiguration>? ProtectProviderMap(
            Dictionary<string, PersistedProviderConfiguration>? map,
            Func<string, IReadOnlyList<ProviderSettingDescriptor>?> descriptorLookup)
        {
            if (map == null)
            {
                return null;
            }

            Dictionary<string, PersistedProviderConfiguration>? protectedMap = null;

            foreach (var (providerName, providerConfig) in map)
            {
                var settings = providerConfig.Settings;
                var descriptors = descriptorLookup(providerName);
                if (settings == null || descriptors == null)
                {
                    continue;
                }

                Dictionary<string, string?>? protectedSettings = null;
                foreach (var descriptor in descriptors)
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
                    protectedMap ??= new Dictionary<string, PersistedProviderConfiguration>(map, StringComparer.OrdinalIgnoreCase);
                    protectedMap[providerName] = new PersistedProviderConfiguration
                    {
                        Settings = protectedSettings,
                        Model = providerConfig.Model
                    };
                }
            }

            return protectedMap;
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
                EnableUserRules = agentConfig.EnableUserRules,
                EnableSkills = agentConfig.EnableSkills
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
            target.EnableSkills = source.EnableSkills ?? true;
        }
    }
}
