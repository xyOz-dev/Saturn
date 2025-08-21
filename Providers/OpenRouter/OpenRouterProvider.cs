using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Saturn.OpenRouter;

namespace Saturn.Providers.OpenRouter
{
    public class OpenRouterProvider : ILLMProvider
    {
        private OpenRouterClient? _client;
        private string? _apiKey;
        
        public OpenRouterProvider()
        {
            // Check environment variable on initialization
            _apiKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
        }
        
        public string Name => "OpenRouter";
        public string Description => "Access multiple AI models through OpenRouter";
        public AuthenticationType AuthType => AuthenticationType.ApiKey;
        
        public bool IsAuthenticated => !string.IsNullOrEmpty(_apiKey);
        
        public async Task<bool> AuthenticateAsync()
        {
            // Check environment variable first
            _apiKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
            
            if (string.IsNullOrEmpty(_apiKey))
            {
                // Load from saved configuration
                var config = await LoadConfigurationInternalAsync();
                if (config != null)
                {
                    _apiKey = config.ApiKey;
                }
            }
            
            if (string.IsNullOrEmpty(_apiKey))
            {
                // In test environments or headless mode, don't prompt for console input
                if (!Console.IsInputRedirected && Environment.UserInteractive)
                {
                    // Prompt user for API key
                    Console.WriteLine("Enter your OpenRouter API key:");
                    var userInput = Console.ReadLine();
                    if (!string.IsNullOrWhiteSpace(userInput))
                    {
                        _apiKey = userInput.Trim();
                    }
                }
            }
            
            if (!string.IsNullOrEmpty(_apiKey))
            {
                InitializeClient();
                await SaveConfigurationAsync();
                return true;
            }
            
            return false;
        }
        
        private void InitializeClient()
        {
            var options = new OpenRouterOptions
            {
                ApiKey = _apiKey,
                Referer = "https://github.com/xyOz-dev/Saturn",
                Title = "Saturn"
            };
            
            _client = new OpenRouterClient(options);
        }
        
        public async Task<ILLMClient> GetClientAsync()
        {
            if (!IsAuthenticated)
            {
                var success = await AuthenticateAsync();
                if (!success)
                {
                    throw new InvalidOperationException("Failed to authenticate with OpenRouter");
                }
            }
            
            if (_client == null)
            {
                InitializeClient();
            }
            
            return new OpenRouterClientWrapper(_client!);
        }
        
        public async Task LogoutAsync()
        {
            _apiKey = null;
            _client?.Dispose();
            _client = null;
            
            // Clear saved configuration
            var configPath = GetConfigPath();
            if (File.Exists(configPath))
            {
                File.Delete(configPath);
            }
            
            await Task.CompletedTask;
        }
        
        public async Task SaveConfigurationAsync()
        {
            if (string.IsNullOrEmpty(_apiKey))
                return;
                
            var config = new ProviderConfiguration
            {
                ProviderName = Name,
                AuthType = AuthType,
                ApiKey = _apiKey,
                LastAuthenticated = DateTime.UtcNow
            };
            
            var configPath = GetConfigPath();
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            
            // Encrypt the API key before saving (use DPAPI on Windows)
            var encrypted = EncryptData(json);
            await File.WriteAllTextAsync(configPath, encrypted);
        }
        
        public async Task LoadConfigurationAsync()
        {
            await AuthenticateAsync();
        }
        
        private async Task<ProviderConfiguration?> LoadConfigurationInternalAsync()
        {
            var configPath = GetConfigPath();
            if (!File.Exists(configPath))
                return null;
                
            try
            {
                var encrypted = await File.ReadAllTextAsync(configPath);
                var json = DecryptData(encrypted);
                return JsonSerializer.Deserialize<ProviderConfiguration>(json);
            }
            catch
            {
                // If decryption fails, return null to trigger re-authentication
                return null;
            }
        }
        
        private string GetConfigPath()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var saturnDir = Path.Combine(appData, "Saturn", "providers");
            Directory.CreateDirectory(saturnDir);
            return Path.Combine(saturnDir, "openrouter.config");
        }
        
        private string EncryptData(string data)
        {
            // TODO: Implement proper encryption using DPAPI or similar
            // For now, just base64 encode (NOT SECURE - replace with real encryption)
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(data));
        }
        
        private string DecryptData(string encrypted)
        {
            // TODO: Implement proper decryption
            return Encoding.UTF8.GetString(Convert.FromBase64String(encrypted));
        }
    }
}