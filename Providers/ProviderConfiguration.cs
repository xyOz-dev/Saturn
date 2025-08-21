using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Saturn.Providers
{
    public class ProviderConfiguration
    {
        public string ProviderName { get; set; } = string.Empty;
        public AuthenticationType AuthType { get; set; }
        public DateTime LastAuthenticated { get; set; }
        
        // For API Key auth
        public string? ApiKey { get; set; }
        
        // For OAuth
        public OAuthTokens? OAuthTokens { get; set; }
        
        // Provider-specific settings
        public Dictionary<string, object> Settings { get; set; } = new();
    }
    
    public class OAuthTokens
    {
        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
        
        [JsonIgnore]
        public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
        
        public bool NeedsRefresh => DateTime.UtcNow >= ExpiresAt.AddMinutes(-5);
    }
}