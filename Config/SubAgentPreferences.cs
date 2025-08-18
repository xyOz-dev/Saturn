using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

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
        
        public string DefaultModel { get; set; } = "anthropic/claude-3.5-sonnet";
        public double DefaultTemperature { get; set; } = 0.3;
        public int DefaultMaxTokens { get; set; } = 4096;
        public double DefaultTopP { get; set; } = 0.95;
        public bool DefaultEnableTools { get; set; } = true;
        
        [JsonPropertyName("purposeModelMappings")]
        public Dictionary<string, string> PurposeModelMappings { get; set; } = new();
        
        [JsonPropertyName("purposeConfigurations")]
        public Dictionary<string, SubAgentConfiguration> PurposeConfigurations { get; set; } = new();
        
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
                    return JsonSerializer.Deserialize<SubAgentPreferences>(json) ?? new SubAgentPreferences();
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
                
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true
                };
                
                var json = JsonSerializer.Serialize(this, options);
                File.WriteAllText(PreferencesPath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to save sub-agent preferences: {ex.Message}");
            }
        }
        
        public SubAgentConfiguration GetConfigurationForPurpose(string purpose)
        {
            if (PurposeConfigurations.TryGetValue(purpose.ToLower(), out var config))
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
            PurposeConfigurations[purpose.ToLower()] = config;
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