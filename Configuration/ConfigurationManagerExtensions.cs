using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace Saturn.Configuration
{
    public static class ConfigurationManagerExtensions
    {
        private static readonly string ProviderConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Saturn",
            "provider.config"
        );
        
        public static async Task<string> GetDefaultProviderAsync()
        {
            if (!File.Exists(ProviderConfigPath))
                return null;
                
            try
            {
                var json = await File.ReadAllTextAsync(ProviderConfigPath);
                var config = JsonSerializer.Deserialize<ProviderPreference>(json);
                return config?.DefaultProvider;
            }
            catch
            {
                return null;
            }
        }
        
        public static async Task SetDefaultProviderAsync(string providerName)
        {
            var config = new ProviderPreference
            {
                DefaultProvider = providerName,
                LastUsed = DateTime.UtcNow
            };
            
            var dir = Path.GetDirectoryName(ProviderConfigPath);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            
            await File.WriteAllTextAsync(ProviderConfigPath, json);
        }
        
        public static async Task ClearDefaultProviderAsync()
        {
            if (File.Exists(ProviderConfigPath))
            {
                File.Delete(ProviderConfigPath);
            }
        }
        
        private class ProviderPreference
        {
            public string DefaultProvider { get; set; }
            public DateTime LastUsed { get; set; }
        }
    }
}