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
        /// <summary>
        /// Config directory: SATURN_CONFIG_DIR when set (portable installs, tests),
        /// otherwise %APPDATA%\Saturn. Resolved per access so the override can change.
        /// </summary>
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

        // Saves are read-modify-write; serialize them so two in-flight saves cannot
        // interleave and drop each other's provider fields.
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

        /// <summary>
        /// Configs written before provider support carry only the flat Model field and
        /// implicitly targeted OpenRouter. Fold them into the per-provider shape.
        /// </summary>
        private static void MigrateLegacyProviderFields(PersistedAgentConfiguration config)
        {
            config.ActiveProvider ??= "openrouter";

            // System.Text.Json deserializes dictionaries with the default case-sensitive
            // comparer; rebuild so "LMStudio" and "lmstudio" cannot become separate keys.
            // Collisions collapse (first entry wins) instead of throwing — a hand-edited
            // file must not abort the whole config load.
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

        /// <summary>Performs the actual save. Callers must hold <see cref="SaveLock"/>.</summary>
        private static async Task SaveConfigurationLockedAsync(PersistedAgentConfiguration config)
        {
            try
            {
                // Most call sites build the persisted object from the live agent
                // configuration, which knows nothing about providers. Carry the provider
                // fields over from the file on disk so those saves don't wipe them.
                if (config.ActiveProvider == null || config.Providers == null)
                {
                    var existing = await LoadConfigurationAsync();
                    config.ActiveProvider ??= existing?.ActiveProvider;
                    config.Providers ??= existing?.Providers;
                }

                // Keep per-provider model memory in sync with the flat model field.
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

                // Write-then-rename so a crash mid-write can never leave a truncated
                // config that a later load would silently treat as "no config".
                var json = JsonSerializer.Serialize(config, JsonOptions);
                var tempPath = ConfigFilePath + ".tmp";
                await File.WriteAllTextAsync(tempPath, json);
                File.Move(tempPath, ConfigFilePath, overwrite: true);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to save configuration: {ex.Message}");
            }
        }

        /// <summary>
        /// Persists the provider choice made in the UI along with the settings it was
        /// connected with and, when known, the model to use on that provider.
        /// </summary>
        public static async Task SaveProviderSelectionAsync(string providerName, ProviderSettings settings, string? model = null)
        {
            // Load-mutate-write must all happen under the lock, or a concurrent save
            // could interleave and clobber the provider changes.
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

            // Secret values that merely mirror the environment variable are not
            // persisted: writing them would store the key in plaintext for no benefit
            // and shadow the env var if it is later rotated.
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
                    settings.Values[kvp.Key] = kvp.Value;
                }
            }
            return settings;
        }

        public static string? GetProviderModel(PersistedAgentConfiguration? config, string providerName)
        {
            if (config?.Providers != null &&
                config.Providers.TryGetValue(providerName, out var providerConfig) &&
                !string.IsNullOrWhiteSpace(providerConfig.Model))
            {
                return providerConfig.Model;
            }

            // Fall back to the flat model only when it belonged to this provider.
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
