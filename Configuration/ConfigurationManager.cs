using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Saturn.Agents.Core;
using Saturn.Configuration.Objects;
using Saturn.Providers;

namespace Saturn.Configuration
{
    public class ConfigurationManager
    {
        private static readonly string AppDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Saturn"
        );

        private static readonly string ConfigFilePath = Path.Combine(AppDataPath, "agent-config.json");

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() }
        };

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
            config.Providers ??= new Dictionary<string, PersistedProviderConfiguration>(StringComparer.OrdinalIgnoreCase);

            if (!config.Providers.TryGetValue(config.ActiveProvider, out var providerConfig))
            {
                providerConfig = new PersistedProviderConfiguration();
                config.Providers[config.ActiveProvider] = providerConfig;
            }

            providerConfig.Model ??= config.Model;
        }

        public static async Task SaveConfigurationAsync(PersistedAgentConfiguration config)
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

                var json = JsonSerializer.Serialize(config, JsonOptions);
                await File.WriteAllTextAsync(ConfigFilePath, json);
            }
            catch (Exception ex)
            {

            }
        }

        /// <summary>
        /// Persists the provider choice made in the UI along with the settings it was
        /// connected with and, when known, the model to use on that provider.
        /// </summary>
        public static async Task SaveProviderSelectionAsync(string providerName, ProviderSettings settings, string? model = null)
        {
            var config = await LoadConfigurationAsync() ?? new PersistedAgentConfiguration();

            config.ActiveProvider = providerName;
            config.Providers ??= new Dictionary<string, PersistedProviderConfiguration>(StringComparer.OrdinalIgnoreCase);

            if (!config.Providers.TryGetValue(providerName, out var providerConfig))
            {
                providerConfig = new PersistedProviderConfiguration();
                config.Providers[providerName] = providerConfig;
            }

            providerConfig.Settings = new Dictionary<string, string?>(settings.Values, StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(model))
            {
                providerConfig.Model = model;
                config.Model = model;
            }

            await SaveConfigurationAsync(config);
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
