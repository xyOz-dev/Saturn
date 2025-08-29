using System;
using System.Threading.Tasks;
using Saturn.Providers;
using Saturn.Providers.Anthropic.Models;

namespace Saturn.Providers.Anthropic
{
    public class AnthropicProvider : ILLMProvider
    {
        private readonly AnthropicAuthService _authService;
        private AnthropicClient? _client;
        private StoredTokens? _currentTokens;
        
        public AnthropicProvider()
        {
            _authService = new AnthropicAuthService();
        }
        
        public string Name => "Anthropic";
        public string Description => "Claude Pro/Max via OAuth authentication";
        public AuthenticationType AuthType => AuthenticationType.OAuth;
        
        public bool IsAuthenticated => _currentTokens != null && !_currentTokens.IsExpired;
        
        public async Task<bool> AuthenticateAsync()
        {
            try
            {
                // Try to load existing tokens
                _currentTokens = await _authService.GetValidTokensAsync();
                
                if (_currentTokens != null)
                {
                    return true;
                }
                
                // Start OAuth flow
                Console.WriteLine("\nSelect authentication method:");
                Console.WriteLine("1. Claude Pro/Max (OAuth)");
                Console.WriteLine("2. Create API Key (requires Anthropic account)");
                Console.Write("Choice (1-2): ");
                
                var choice = Console.ReadLine();
                var useClaudeMax = choice != "2";
                
                var success = await _authService.AuthenticateAsync(useClaudeMax);
                
                if (success)
                {
                    _currentTokens = await _authService.GetValidTokensAsync();
                    return true;
                }
                
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Authentication failed: {ex.Message}");
                return false;
            }
        }
        
        public async Task<ILLMClient> GetClientAsync()
        {
            if (!IsAuthenticated)
            {
                var authenticated = await AuthenticateAsync();
                if (!authenticated)
                {
                    throw new InvalidOperationException("Failed to authenticate with Anthropic");
                }
            }
            
            // Ensure tokens are fresh
            _currentTokens = await _authService.GetValidTokensAsync();
            
            if (_client == null)
            {
                _client = new AnthropicClient(_authService);
            }
            
            return new AnthropicClientWrapper(_client);
        }
        
        public async Task LogoutAsync()
        {
            _authService.Logout();
            _currentTokens = null;
            _client = null;
            await Task.CompletedTask;
        }
        
        public async Task SaveConfigurationAsync()
        {
            // Tokens are already saved by the auth service
            await Task.CompletedTask;
        }
        
        public async Task LoadConfigurationAsync()
        {
            _currentTokens = await _authService.GetValidTokensAsync();
        }
    }
}