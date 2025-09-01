using System;
using System.Threading.Tasks;
using Saturn.Providers;
using Saturn.Providers.Anthropic.Models;
using Saturn.UI.Dialogs;
using Terminal.Gui;

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
                
                // Use UI dialog if Terminal.Gui is initialized, otherwise fall back to console
                bool useClaudeMax = true;
                bool success = false;
                
                if (Application.Top != null)
                {
                    // Start the OAuth flow to generate PKCE and get the auth URL
                    success = await _authService.AuthenticateWithDialogAsync();
                }
                else if (!Console.IsInputRedirected && Environment.UserInteractive)
                {
                    // Fall back to console input
                    Console.WriteLine("\nSelect authentication method:");
                    Console.WriteLine("1. Claude Pro/Max (OAuth)");
                    Console.WriteLine("2. Create API Key (requires Anthropic account)");
                    Console.Write("Choice (1-2): ");
                    
                    var choice = Console.ReadLine();
                    useClaudeMax = choice != "2";
                    
                    success = await _authService.AuthenticateAsync(useClaudeMax);
                }
                else
                {
                    // Non-interactive environment
                    return false;
                }
                
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