using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Saturn.OpenRouter.Serialization;

namespace Saturn.Config
{
    public class SubAgentPreferences
    {
        private static SubAgentPreferences? _instance;
        private static readonly string PreferencesPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Saturn",
            "subagent-preferences.json"
        );
        
        private static readonly JsonSerializerOptions JsonOptions = Json.CreateDefaultOptions(
            useCamelCase: true,
            ignoreNulls: true,
            allowTrailingCommas: true,
            caseInsensitive: true
        );
        
        public string DefaultModel { get; set; } = "anthropic/claude-3.5-sonnet";
        public double DefaultTemperature { get; set; } = 0.3;
        public int DefaultMaxTokens { get; set; } = 4096;
        public double DefaultTopP { get; set; } = 0.95;
        public bool DefaultEnableTools { get; set; } = true;
        
        [JsonPropertyName("purposeModelMappings")]
        public Dictionary<string, string> PurposeModelMappings { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        
        [JsonPropertyName("purposeConfigurations")]
        public Dictionary<string, SubAgentConfiguration> PurposeConfigurations { get; set; } = new Dictionary<string, SubAgentConfiguration>(StringComparer.OrdinalIgnoreCase);
        
        public static SubAgentPreferences Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = Load();
                }
                return _instance;
            }
        }
        
        private static SubAgentPreferences Load()
        {
            try
            {
                if (File.Exists(PreferencesPath))
                {
                    var json = File.ReadAllText(PreferencesPath);
                    var loaded = JsonSerializer.Deserialize<SubAgentPreferences>(json, JsonOptions);
                    if (loaded != null)
                    {
                        var tempModelMappings = loaded.PurposeModelMappings ?? new Dictionary<string, string>();
                        loaded.PurposeModelMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var kvp in tempModelMappings)
                        {
                            loaded.PurposeModelMappings[kvp.Key] = kvp.Value;
                        }
                        
                        var tempConfigurations = loaded.PurposeConfigurations ?? new Dictionary<string, SubAgentConfiguration>();
                        loaded.PurposeConfigurations = new Dictionary<string, SubAgentConfiguration>(StringComparer.OrdinalIgnoreCase);
                        foreach (var kvp in tempConfigurations)
                        {
                            loaded.PurposeConfigurations[kvp.Key] = kvp.Value;
                        }
                        
                        return loaded;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load sub-agent preferences: {ex.Message}");
            }
            
            return new SubAgentPreferences();
        }
        
        public void Save()
        {
            try
            {
                var directory = Path.GetDirectoryName(PreferencesPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                
                var saveOptions = new JsonSerializerOptions(JsonOptions)
                {
                    WriteIndented = true
                };
                
                var json = JsonSerializer.Serialize(this, saveOptions);
                File.WriteAllText(PreferencesPath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to save sub-agent preferences: {ex.Message}");
            }
        }
        
        public SubAgentConfiguration GetConfigurationForPurpose(string purpose)
        {
            if (string.IsNullOrWhiteSpace(purpose))
            {
                throw new ArgumentException("Purpose cannot be null or whitespace.", nameof(purpose));
            }
            
            if (PurposeConfigurations.TryGetValue(purpose, out var config))
            {
                return config;
            }
            
            return new SubAgentConfiguration
            {
                Model = DefaultModel,
                Temperature = DefaultTemperature,
                MaxTokens = DefaultMaxTokens,
                TopP = DefaultTopP,
                EnableTools = DefaultEnableTools
            };
        }
        
        public void SetConfigurationForPurpose(string purpose, SubAgentConfiguration config)
        {
            if (string.IsNullOrWhiteSpace(purpose))
            {
                throw new ArgumentException("Purpose cannot be null or whitespace.", nameof(purpose));
            }
            
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config), "Configuration cannot be null.");
            }
            
            PurposeConfigurations[purpose] = config;
            Save();
        }
    }
    
    public class SubAgentConfiguration
    {
        public string Model { get; set; } = "anthropic/claude-3.5-sonnet";
        public double Temperature { get; set; } = 0.3;
        public int MaxTokens { get; set; } = 4096;
        public double TopP { get; set; } = 0.95;
        public bool EnableTools { get; set; } = true;
        public string? SystemPromptOverride { get; set; }
    }
}