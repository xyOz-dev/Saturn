using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Saturn.Agents.Core;
using Saturn.Configuration.Objects;

namespace Saturn.Configuration
{
    public class ConfigurationManager
    {
        private static readonly Regex providerNameRegex =
            new(@"^[a-zA-Z0-9\-_]+$", RegexOptions.Compiled);
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
                
                return config;
            }
            catch (Exception)
            {

                return null;
            }
        }

        public static async Task SaveConfigurationAsync(PersistedAgentConfiguration config)
        {
            // Validate input parameters
            if (config == null)
                throw new ArgumentNullException(nameof(config));
            
            // Validate configuration values
            ValidateConfiguration(config);
            
            try
            {
                if (!Directory.Exists(AppDataPath))
                {
                    Directory.CreateDirectory(AppDataPath);
                }

                var json = JsonSerializer.Serialize(config, JsonOptions);
                
                if (string.IsNullOrEmpty(json))
                    throw new InvalidOperationException("Failed to serialize configuration to JSON");
                
                // Use atomic write pattern to prevent corruption
                await WriteFileAtomicallyAsync(ConfigFilePath, json);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to save configuration: {ex.Message}", ex);
            }
        }
        
        private static async Task WriteFileAtomicallyAsync(string filePath, string content)
        {
            var tempPath = filePath + ".tmp";
            
            try
            {
                // Write to temporary file first
                await File.WriteAllTextAsync(tempPath, content);
                
                // Atomically replace the original file
#if NET8_0_OR_GREATER
                File.Move(tempPath, filePath, overwrite: true);
#else
                // For older versions, delete and move
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
                File.Move(tempPath, filePath);
#endif
            }
            catch
            {
                // Clean up temporary file if something went wrong
                if (File.Exists(tempPath))
                {
                    try
                    {
                        File.Delete(tempPath);
                    }
                    catch
                    {
                        // Ignore cleanup errors
                    }
                }
                throw;
            }
        }

        public static PersistedAgentConfiguration FromAgentConfiguration(AgentConfiguration agentConfig)
        {
            if (agentConfig == null)
                throw new ArgumentNullException(nameof(agentConfig));
            
            var config = new PersistedAgentConfiguration
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
            
            // Validate the created configuration
            ValidateConfiguration(config);
            
            return config;
        }

        public static void ApplyToAgentConfiguration(AgentConfiguration target, PersistedAgentConfiguration source)
        {
            if (target == null)
                throw new ArgumentNullException(nameof(target));
            
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            
            // Validate source configuration before applying
            ValidateConfiguration(source);
            
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
        
        private static void ValidateConfiguration(PersistedAgentConfiguration config)
        {
            // Validate name
            if (!string.IsNullOrEmpty(config.Name) && string.IsNullOrWhiteSpace(config.Name))
                throw new ArgumentException("Configuration name cannot be whitespace only");
            
            // Validate model
            if (!string.IsNullOrEmpty(config.Model) && string.IsNullOrWhiteSpace(config.Model))
                throw new ArgumentException("Model name cannot be whitespace only");
            
            // Validate temperature
            if (config.Temperature.HasValue && (config.Temperature.Value < 0 || config.Temperature.Value > 2))
                throw new ArgumentException("Temperature must be between 0 and 2");
            
            // Validate max tokens
            if (config.MaxTokens.HasValue && config.MaxTokens.Value <= 0)
                throw new ArgumentException("MaxTokens must be greater than 0");
            
            // Validate top P
            if (config.TopP.HasValue && (config.TopP.Value < 0 || config.TopP.Value > 1))
                throw new ArgumentException("TopP must be between 0 and 1 (inclusive)");
            
            // Validate max history messages
            if (config.MaxHistoryMessages.HasValue && config.MaxHistoryMessages.Value < 0)
                throw new ArgumentException("MaxHistoryMessages cannot be negative");
            
            // Validate tool names
            if (config.ToolNames != null)
            {
                foreach (var toolName in config.ToolNames)
                {
                    if (string.IsNullOrWhiteSpace(toolName))
                        throw new ArgumentException("Tool names cannot be null, empty, or whitespace only");
                }
            }
            
            // Validate provider name
            if (!string.IsNullOrEmpty(config.ProviderName))
            {
                if (string.IsNullOrWhiteSpace(config.ProviderName))
                    throw new ArgumentException("Provider name cannot be whitespace only");
                
                // Validate provider name format
                if (!providerNameRegex.IsMatch(config.ProviderName))
                    throw new ArgumentException("Provider name must contain only alphanumeric characters, hyphens, and underscores");
            }
        }
    }
}