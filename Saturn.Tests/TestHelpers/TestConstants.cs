using System;
using SaturnFork.Providers.Anthropic.Models;

namespace Saturn.Tests.TestHelpers
{
    public static class TestConstants
    {
        public const string TestClientId = "test-client-id";
        public const string TestAccessToken = "test-access-token";
        public const string TestRefreshToken = "test-refresh-token";
        public const string TestAuthCode = "test-auth-code#test-state";
        public const string TestPKCEVerifier = "test-verifier-123456789";
        public const string TestPKCEChallenge = "test-challenge-abc";
        
        public static readonly DateTime TestExpiryTime = new DateTime(2024, 12, 31, 23, 59, 59, DateTimeKind.Utc);
        
        public static StoredTokens CreateValidTokens()
        {
            return new StoredTokens
            {
                AccessToken = TestAccessToken,
                RefreshToken = TestRefreshToken,
                ExpiresAt = DateTime.UtcNow.AddHours(1),
                CreatedAt = DateTime.UtcNow
            };
        }
        
        public static StoredTokens CreateExpiredTokens()
        {
            return new StoredTokens
            {
                AccessToken = TestAccessToken,
                RefreshToken = TestRefreshToken,
                ExpiresAt = DateTime.UtcNow.AddHours(-1),
                CreatedAt = DateTime.UtcNow.AddHours(-2)
            };
        }
        
        public static StoredTokens CreateTokensNeedingRefresh()
        {
            return new StoredTokens
            {
                AccessToken = TestAccessToken,
                RefreshToken = TestRefreshToken,
                ExpiresAt = DateTime.UtcNow.AddMinutes(2), // Within 5 minute refresh window
                CreatedAt = DateTime.UtcNow.AddHours(-1)
            };
        }
    }
}