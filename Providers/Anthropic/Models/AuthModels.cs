using System;
using System.Text.Json.Serialization;

namespace Saturn.Providers.Anthropic.Models
{
    public class OAuthTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;
        
        [JsonPropertyName("refresh_token")]
        public string RefreshToken { get; set; } = string.Empty;
        
        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
        
        [JsonPropertyName("token_type")]
        public string TokenType { get; set; } = string.Empty;
    }
    
    public class OAuthTokenRequest
    {
        [JsonPropertyName("grant_type")]
        public string GrantType { get; set; } = string.Empty;
        
        [JsonPropertyName("code")]
        public string Code { get; set; } = string.Empty;
        
        [JsonPropertyName("state")]
        public string State { get; set; } = string.Empty;
        
        [JsonPropertyName("client_id")]
        public string ClientId { get; set; } = string.Empty;
        
        [JsonPropertyName("redirect_uri")]
        public string RedirectUri { get; set; } = string.Empty;
        
        [JsonPropertyName("code_verifier")]
        public string CodeVerifier { get; set; } = string.Empty;
    }
    
    public class OAuthRefreshRequest
    {
        [JsonPropertyName("grant_type")]
        public string GrantType { get; set; } = "refresh_token";
        
        [JsonPropertyName("refresh_token")]
        public string RefreshToken { get; set; } = string.Empty;
        
        [JsonPropertyName("client_id")]
        public string ClientId { get; set; } = string.Empty;
    }
    
    public class StoredTokens
    {
        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
        public DateTime CreatedAt { get; set; }
        
        public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
        public bool NeedsRefresh => DateTime.UtcNow >= ExpiresAt.AddMinutes(-5);
    }
}