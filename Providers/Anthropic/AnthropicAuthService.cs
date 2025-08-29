using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Saturn.Providers.Anthropic.Models;
using Saturn.Providers.Anthropic.Utils;

namespace Saturn.Providers.Anthropic
{
    public class AnthropicAuthService : IDisposable
    {
        // OAuth Configuration Constants
        private const string CLIENT_ID = "9d1c250a-e61b-44d9-88ed-5944d1962f5e";
        private const string AUTH_URL_CLAUDE = "https://claude.ai/oauth/authorize";
        private const string AUTH_URL_CONSOLE = "https://console.anthropic.com/oauth/authorize";
        private const string TOKEN_URL = "https://console.anthropic.com/v1/oauth/token";
        private const string REDIRECT_URI = "https://console.anthropic.com/oauth/code/callback";
        private const string SCOPES = "org:create_api_key user:profile user:inference";
        
        private readonly HttpClient _httpClient;
        private readonly TokenStore _tokenStore;
        private PKCEGenerator.PKCEPair _currentPKCE;
        private string _currentStateToken;
        
        public AnthropicAuthService()
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            // No User-Agent header - matches @ai-sdk/anthropic behavior
            _tokenStore = new TokenStore();
        }
        
        public async Task<bool> AuthenticateAsync(bool useClaudeMax = true)
        {
            try
            {
                // Check for existing valid tokens
                var storedTokens = await _tokenStore.LoadTokensAsync();
                if (storedTokens != null && !storedTokens.IsExpired)
                {
                    Console.WriteLine("Using existing authentication tokens.");
                    return true;
                }
                
                // If tokens exist but are expired, try to refresh
                if (storedTokens != null && !string.IsNullOrEmpty(storedTokens.RefreshToken))
                {
                    Console.WriteLine("Refreshing expired tokens...");
                    var refreshed = await RefreshTokensAsync(storedTokens.RefreshToken);
                    if (refreshed != null)
                    {
                        Console.WriteLine("Tokens refreshed successfully.");
                        return true;
                    }
                }
                
                // Start new OAuth flow
                return await StartOAuthFlowAsync(useClaudeMax);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Authentication error: {ex.Message}");
                return false;
            }
        }
        
        private async Task<bool> StartOAuthFlowAsync(bool useClaudeMax)
        {
            // Generate PKCE pair for security
            _currentPKCE = PKCEGenerator.Generate();
            
            // Generate separate state token for CSRF protection
            _currentStateToken = GenerateStateToken();
            
            // Build authorization URL
            var authUrl = BuildAuthorizationUrl(useClaudeMax);
            
            // Display instructions
            Console.WriteLine("\n" + new string('=', 50));
            Console.WriteLine("    Anthropic Authentication");
            Console.WriteLine(new string('=', 50));
            Console.WriteLine($"\nAuthenticating with: {(useClaudeMax ? "Claude Pro/Max" : "Anthropic API")}");
            Console.WriteLine("\nOpening browser for authentication...");
            Console.WriteLine("\nIf the browser doesn't open automatically, please visit:");
            Console.WriteLine($"\n{authUrl}\n");
            
            // Open browser
            var browserOpened = BrowserLauncher.OpenUrl(authUrl);
            if (!browserOpened)
            {
                Console.WriteLine("⚠️  Could not open browser automatically.");
            }
            
            // Wait for user to complete login
            Console.WriteLine("After logging in, you'll see an authorization code.");
            Console.WriteLine("The code may appear as: CODE#STATE");
            Console.WriteLine("\nPlease paste the ENTIRE code (including # if present) below:");
            Console.Write("> ");
            
            var authCode = Console.ReadLine()?.Trim();
            
            if (string.IsNullOrEmpty(authCode))
            {
                Console.WriteLine("\n❌ No authorization code provided. Authentication cancelled.");
                return false;
            }
            
            Console.WriteLine("\nExchanging authorization code for tokens...");
            
            // Exchange code for tokens
            var tokens = await ExchangeCodeForTokensAsync(authCode);
            if (tokens != null)
            {
                Console.WriteLine("✅ Successfully authenticated with Anthropic!");
                return true;
            }
            else
            {
                Console.WriteLine("❌ Failed to authenticate. Please try again.");
                return false;
            }
        }
        
        private string BuildAuthorizationUrl(bool useClaudeMax)
        {
            var baseUrl = useClaudeMax ? AUTH_URL_CLAUDE : AUTH_URL_CONSOLE;
            var parameters = new Dictionary<string, string>
            {
                ["code"] = "true",
                ["client_id"] = CLIENT_ID,
                ["response_type"] = "code",
                ["redirect_uri"] = REDIRECT_URI,
                ["scope"] = SCOPES,
                ["code_challenge"] = _currentPKCE.Challenge,
                ["code_challenge_method"] = "S256",
                ["state"] = _currentStateToken
            };
            
            var queryString = string.Join("&", 
                parameters.Select(kvp => $"{kvp.Key}={Uri.EscapeDataString(kvp.Value)}"));
            
            return $"{baseUrl}?{queryString}";
        }
        
        private async Task<StoredTokens> ExchangeCodeForTokensAsync(string authCode)
        {
            // Validate input parameters
            if (string.IsNullOrEmpty(authCode))
                throw new ArgumentException("Authorization code cannot be null or empty", nameof(authCode));
            
            if (string.IsNullOrWhiteSpace(authCode))
                throw new ArgumentException("Authorization code cannot be whitespace only", nameof(authCode));
            
            if (_currentPKCE == null)
                throw new InvalidOperationException("PKCE pair not initialized. Call StartOAuthFlowAsync first.");
            
            if (string.IsNullOrEmpty(_currentPKCE.Verifier))
                throw new InvalidOperationException("PKCE verifier is invalid");
            
            try
            {
                // Parse code and state if combined with #
                string code = authCode.Trim();
                string receivedState = null;
                
                if (code.Contains("#"))
                {
                    var parts = code.Split('#', 2);
                    code = parts[0];
                    if (parts.Length > 1)
                    {
                        receivedState = parts[1];
                    }
                }
                
                // Validate parsed code
                if (string.IsNullOrEmpty(code))
                    throw new ArgumentException("Parsed authorization code is empty");
                
                // Validate state parameter for CSRF protection (optional for backward compatibility)
                if (!string.IsNullOrEmpty(receivedState) && !string.IsNullOrEmpty(_currentStateToken))
                {
                    if (receivedState != _currentStateToken)
                    {
                        Console.WriteLine("⚠️  Warning: State parameter mismatch detected. This could indicate a security issue.");
                        throw new ArgumentException("Invalid state parameter - possible CSRF attack");
                    }
                }
                else if (string.IsNullOrEmpty(receivedState))
                {
                    Console.WriteLine("⚠️  Warning: State parameter not provided in OAuth response. Proceeding without state validation.");
                }
                else if (string.IsNullOrEmpty(_currentStateToken))
                {
                    Console.WriteLine("⚠️  Warning: Current state token is missing. Proceeding without state validation.");
                }
                
                // Prepare token request
                var request = new
                {
                    grant_type = "authorization_code",
                    code = code,
                    state = receivedState ?? _currentStateToken, // Include state in token request
                    client_id = CLIENT_ID,
                    redirect_uri = REDIRECT_URI,
                    code_verifier = _currentPKCE.Verifier
                };
                
                var json = JsonSerializer.Serialize(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                // Send token request
                var response = await _httpClient.PostAsync(TOKEN_URL, content);
                var responseBody = await response.Content.ReadAsStringAsync();
                
                if (response.IsSuccessStatusCode)
                {
                    if (string.IsNullOrEmpty(responseBody))
                        throw new InvalidOperationException("Token response body is empty");
                    
                    var tokenResponse = JsonSerializer.Deserialize<OAuthTokenResponse>(responseBody);
                    
                    if (tokenResponse == null)
                        throw new InvalidOperationException("Failed to deserialize token response");
                    
                    // Validate token response
                    if (string.IsNullOrEmpty(tokenResponse.AccessToken))
                        throw new InvalidOperationException("Access token is missing from response");
                    
                    if (tokenResponse.ExpiresIn <= 0)
                        throw new InvalidOperationException("Invalid token expiration time");
                    
                    // Create stored tokens with validation
                    var storedTokens = new StoredTokens
                    {
                        AccessToken = tokenResponse.AccessToken,
                        RefreshToken = tokenResponse.RefreshToken,
                        ExpiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn),
                        CreatedAt = DateTime.UtcNow
                    };
                    
                    // Additional validation of created tokens
                    if (storedTokens.ExpiresAt <= DateTime.UtcNow)
                        throw new InvalidOperationException("Token expiration time is in the past");
                    
                    // Save tokens securely
                    await _tokenStore.SaveTokensAsync(storedTokens);
                    return storedTokens;
                }
                else
                {
                    Console.WriteLine($"Token exchange failed: HTTP {response.StatusCode}");
                    Console.WriteLine($"Response: {responseBody}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error exchanging code for tokens: {ex.Message}");
                return null;
            }
        }
        
        public async Task<StoredTokens> RefreshTokensAsync(string refreshToken)
        {
            // Validate input parameters
            if (string.IsNullOrEmpty(refreshToken))
                throw new ArgumentException("Refresh token cannot be null or empty", nameof(refreshToken));
            
            if (string.IsNullOrWhiteSpace(refreshToken))
                throw new ArgumentException("Refresh token cannot be whitespace only", nameof(refreshToken));
            
            try
            {
                var request = new
                {
                    grant_type = "refresh_token",
                    refresh_token = refreshToken.Trim(),
                    client_id = CLIENT_ID
                };
                
                var json = JsonSerializer.Serialize(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                var response = await _httpClient.PostAsync(TOKEN_URL, content);
                
                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync();
                    
                    if (string.IsNullOrEmpty(responseJson))
                        throw new InvalidOperationException("Refresh token response body is empty");
                    
                    var tokenResponse = JsonSerializer.Deserialize<OAuthTokenResponse>(responseJson);
                    
                    if (tokenResponse == null)
                        throw new InvalidOperationException("Failed to deserialize refresh token response");
                    
                    // Validate token response
                    if (string.IsNullOrEmpty(tokenResponse.AccessToken))
                        throw new InvalidOperationException("Access token is missing from refresh response");
                    
                    if (tokenResponse.ExpiresIn <= 0)
                        throw new InvalidOperationException("Invalid token expiration time in refresh response");
                    
                    var storedTokens = new StoredTokens
                    {
                        AccessToken = tokenResponse.AccessToken,
                        RefreshToken = tokenResponse.RefreshToken ?? refreshToken.Trim(),
                        ExpiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn),
                        CreatedAt = DateTime.UtcNow
                    };
                    
                    // Validate created tokens
                    if (storedTokens.ExpiresAt <= DateTime.UtcNow)
                        throw new InvalidOperationException("Refreshed token expiration time is in the past");
                    
                    await _tokenStore.SaveTokensAsync(storedTokens);
                    return storedTokens;
                }
                else
                {
                    Console.WriteLine("Token refresh failed. Re-authentication required.");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error refreshing tokens: {ex.Message}");
                return null;
            }
        }
        
        public async Task<StoredTokens> GetValidTokensAsync()
        {
            var tokens = await _tokenStore.LoadTokensAsync();
            
            if (tokens == null)
                return null;
                
            // Refresh if needed (5 minutes before expiry)
            if (tokens.NeedsRefresh && !string.IsNullOrEmpty(tokens.RefreshToken))
            {
                var refreshed = await RefreshTokensAsync(tokens.RefreshToken);
                if (refreshed != null)
                {
                    return refreshed;
                }
            }
            
            if (!tokens.IsExpired)
            {
                return tokens;
            }
            
            return null;
        }
        
        public void Logout()
        {
            _tokenStore.DeleteTokens();
            _currentPKCE = null;
            _currentStateToken = null;
        }
        
        private string GenerateStateToken()
        {
            // Generate a cryptographically secure random state token
            var tokenBytes = new byte[32]; // 256-bit token
            using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
            {
                rng.GetBytes(tokenBytes);
            }
            
            // Convert to URL-safe base64 string
            return Convert.ToBase64String(tokenBytes)
                .Replace("+", "-")
                .Replace("/", "_")
                .TrimEnd('=');
        }
        
        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}