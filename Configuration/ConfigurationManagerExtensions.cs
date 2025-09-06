using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace Saturn.Configuration
{
    public static class ConfigurationManagerExtensions
    {
        private static readonly string ProviderConfigPath = GetProviderConfigPath();
        
        private static string GetProviderConfigPath()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            
            // Fallback for environments where ApplicationData is not set (like in tests on Linux)
            if (string.IsNullOrEmpty(appData))
            {
                appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
                if (string.IsNullOrEmpty(appData) || !Directory.Exists(Path.GetDirectoryName(appData)))
                {
                    // Ultimate fallback for test environments
                    appData = Path.GetTempPath();
                }
            }
            
            return Path.Combine(appData, "Saturn", "provider.config");
        }
        
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