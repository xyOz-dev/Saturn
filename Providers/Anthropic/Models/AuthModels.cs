using System;
using System.Text.Json.Serialization;

namespace Saturn.Providers.Anthropic.Models
{
    public class OAuthTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; }
        
        [JsonPropertyName("refresh_token")]
        public string RefreshToken { get; set; }
        
        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
        
        [JsonPropertyName("token_type")]
        public string TokenType { get; set; }
    }
    
    public class OAuthTokenRequest
    {
        [JsonPropertyName("grant_type")]
        public string GrantType { get; set; }
        
        [JsonPropertyName("code")]
        public string Code { get; set; }
        
        [JsonPropertyName("state")]
        public string State { get; set; }
        
        [JsonPropertyName("client_id")]
        public string ClientId { get; set; }
        
        [JsonPropertyName("redirect_uri")]
        public string RedirectUri { get; set; }
        
        [JsonPropertyName("code_verifier")]
        public string CodeVerifier { get; set; }
    }
    
    public class OAuthRefreshRequest
    {
        [JsonPropertyName("grant_type")]
        public string GrantType { get; set; } = "refresh_token";
        
        [JsonPropertyName("refresh_token")]
        public string RefreshToken { get; set; }
        
        [JsonPropertyName("client_id")]
        public string ClientId { get; set; }
    }
    
    public class StoredTokens
    {
        public string AccessToken { get; set; }
        public string RefreshToken { get; set; }
        public DateTime ExpiresAt { get; set; }
        public DateTime CreatedAt { get; set; }
        
        public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
        public bool NeedsRefresh => DateTime.UtcNow >= ExpiresAt.AddMinutes(-5);
    }
}