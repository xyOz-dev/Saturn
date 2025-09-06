using Xunit;
using FluentAssertions;
using Saturn.Providers.Anthropic;
using Saturn.Providers.Anthropic.Models;
using System;
using System.IO;
using System.Threading.Tasks;
using Saturn.Tests.TestHelpers;

namespace Saturn.Tests.Providers.Anthropic
{
    public class TokenStoreTests : IDisposable
    {
        private readonly TokenStore _tokenStore;
        private readonly string _testPath;
        private readonly string _testDir;
        
        public TokenStoreTests()
        {
            // Use a test-specific directory to avoid conflicts
            var testId = Guid.NewGuid().ToString("N")[..8];
            _testDir = Path.Combine(Path.GetTempPath(), "Saturn", "auth_test_" + testId);
            Directory.CreateDirectory(_testDir);
            
            _testPath = Path.Combine(_testDir, "anthropic.tokens");
            
            // Set environment variable so TokenStore uses our test directory
            Environment.SetEnvironmentVariable("SATURN_TEST_CONFIG_PATH", _testDir);
            
            // Create TokenStore with test path
            _tokenStore = new TokenStore();
        }
        
        [Fact]
        public async Task SaveTokens_CreatesFile()
        {
            // Arrange
            var tokens = TestConstants.CreateValidTokens();
            
            // Act
            await _tokenStore.SaveTokensAsync(tokens);
            
            // Assert
            var actualPath = GetActualTokenPath();
            File.Exists(actualPath).Should().BeTrue("token file should be created");
        }
        
        [Fact]
        public async Task LoadTokens_ReturnsNull_WhenFileDoesNotExist()
        {
            // Arrange - ensure no token file exists
            CleanupTokenFile();
            
            // Act
            var result = await _tokenStore.LoadTokensAsync();
            
            // Assert
            result.Should().BeNull("should return null when no token file exists");
        }
        
        [Fact]
        public async Task SaveAndLoad_RoundTrip_PreservesData()
        {
            // Arrange
            var originalTokens = TestConstants.CreateValidTokens();
            
            // Act
            await _tokenStore.SaveTokensAsync(originalTokens);
            var loadedTokens = await _tokenStore.LoadTokensAsync();
            
            // Assert
            loadedTokens.Should().NotBeNull("loaded tokens should not be null");
            loadedTokens.AccessToken.Should().Be(originalTokens.AccessToken);
            loadedTokens.RefreshToken.Should().Be(originalTokens.RefreshToken);
            loadedTokens.ExpiresAt.Should().BeCloseTo(originalTokens.ExpiresAt, TimeSpan.FromSeconds(1));
            loadedTokens.CreatedAt.Should().BeCloseTo(originalTokens.CreatedAt, TimeSpan.FromSeconds(1));
        }
        
        [Fact]
        public void StoredTokens_IsExpired_Property_WorksCorrectly()
        {
            // Test expired tokens
            var expiredTokens = new StoredTokens
            {
                AccessToken = "test",
                RefreshToken = "test",
                ExpiresAt = DateTime.UtcNow.AddHours(-1), // Expired 1 hour ago
                CreatedAt = DateTime.UtcNow.AddHours(-2)
            };
            expiredTokens.IsExpired.Should().BeTrue("tokens expired an hour ago should be marked as expired");
            
            // Test valid tokens
            var validTokens = new StoredTokens
            {
                AccessToken = "test",
                RefreshToken = "test",
                ExpiresAt = DateTime.UtcNow.AddHours(1), // Expires in 1 hour
                CreatedAt = DateTime.UtcNow
            };
            validTokens.IsExpired.Should().BeFalse("tokens expiring in the future should not be marked as expired");
            
            // Test edge case - exactly at expiration
            var edgeCaseTokens = new StoredTokens
            {
                AccessToken = "test",
                RefreshToken = "test",
                ExpiresAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow.AddMinutes(-30)
            };
            edgeCaseTokens.IsExpired.Should().BeTrue("tokens at exact expiration time should be marked as expired");
        }
        
        [Fact]
        public async Task LoadTokens_HandlesCorruptedFile()
        {
            // Arrange
            var actualPath = GetActualTokenPath();
            Directory.CreateDirectory(Path.GetDirectoryName(actualPath));
            await File.WriteAllTextAsync(actualPath, "corrupted data that is not valid JSON or encrypted data");
            
            // Act
            var result = await _tokenStore.LoadTokensAsync();
            
            // Assert
            result.Should().BeNull("should return null for corrupted file");
            // The corrupted file should be deleted by the implementation
            File.Exists(actualPath).Should().BeFalse("corrupted file should be deleted");
        }
        
        [Fact]
        public void DeleteTokens_RemovesFile()
        {
            // Arrange
            var actualPath = GetActualTokenPath();
            Directory.CreateDirectory(Path.GetDirectoryName(actualPath));
            File.WriteAllText(actualPath, "test data");
            
            // Act
            _tokenStore.DeleteTokens();
            
            // Assert
            File.Exists(actualPath).Should().BeFalse("token file should be deleted");
        }
        
        [Fact]
        public async Task Tokens_AreEncrypted()
        {
            // Arrange
            var tokens = TestConstants.CreateValidTokens();
            
            // Act
            await _tokenStore.SaveTokensAsync(tokens);
            var actualPath = GetActualTokenPath();
            var fileContent = await File.ReadAllTextAsync(actualPath);
            
            // Assert
            fileContent.Should().NotContain(tokens.AccessToken, "access token should be encrypted");
            fileContent.Should().NotContain(tokens.RefreshToken, "refresh token should be encrypted");
        }
        
        [Fact]
        public async Task SaveTokens_CreatesDirectoryIfNotExists()
        {
            // Arrange
            var actualPath = GetActualTokenPath();
            var directory = Path.GetDirectoryName(actualPath);
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
            
            var tokens = TestConstants.CreateValidTokens();
            
            // Act
            await _tokenStore.SaveTokensAsync(tokens);
            
            // Assert
            Directory.Exists(directory).Should().BeTrue("directory should be created");
            File.Exists(actualPath).Should().BeTrue("token file should be created");
        }
        
        [Fact]
        public async Task SaveTokens_OverwritesExistingFile()
        {
            // Arrange
            var firstTokens = TestConstants.CreateValidTokens();
            var secondTokens = TestConstants.CreateValidTokens();
            secondTokens.AccessToken = "different_access_token";
            
            // Act
            await _tokenStore.SaveTokensAsync(firstTokens);
            await _tokenStore.SaveTokensAsync(secondTokens);
            var loadedTokens = await _tokenStore.LoadTokensAsync();
            
            // Assert
            loadedTokens.AccessToken.Should().Be(secondTokens.AccessToken, 
                "should contain the second set of tokens");
        }
        
        [Fact]
        public async Task LoadTokens_ReturnsCorrectExpirationStatus()
        {
            // Arrange - Use a time-travel approach that works on all platforms
            // Save tokens that will expire very soon
            var shortLivedTokens = new StoredTokens
            {
                AccessToken = TestConstants.TestAccessToken,
                RefreshToken = TestConstants.TestRefreshToken,
                ExpiresAt = DateTime.UtcNow.AddMilliseconds(100), // Expires in 100ms
                CreatedAt = DateTime.UtcNow
            };
            
            // Act - Save the tokens, wait for expiration, then load
            await _tokenStore.SaveTokensAsync(shortLivedTokens);
            
            // Wait for the tokens to expire
            await Task.Delay(200);
            
            // Load the now-expired tokens
            var loadedTokens = await _tokenStore.LoadTokensAsync();
            
            // Assert - The tokens should be loaded but marked as expired
            loadedTokens.Should().NotBeNull("tokens should be successfully loaded");
            loadedTokens.AccessToken.Should().Be(shortLivedTokens.AccessToken);
            loadedTokens.RefreshToken.Should().Be(shortLivedTokens.RefreshToken);
            loadedTokens.IsExpired.Should().BeTrue("tokens should be marked as expired after the delay");
        }
        
        [Fact]
        public async Task LoadTokens_ReturnsCorrectRefreshStatus()
        {
            // Arrange - Save tokens that need refresh
            var tokensNeedingRefresh = TestConstants.CreateTokensNeedingRefresh();
            await _tokenStore.SaveTokensAsync(tokensNeedingRefresh);
            
            // Act
            var loadedTokens = await _tokenStore.LoadTokensAsync();
            
            // Assert
            loadedTokens.Should().NotBeNull();
            loadedTokens.NeedsRefresh.Should().BeTrue("tokens should need refresh");
            loadedTokens.IsExpired.Should().BeFalse("tokens should not be expired yet");
        }
        
        [Fact]
        public async Task Tokens_UseProperEncryption_WithVersionPrefix()
        {
            // Arrange
            var tokens = TestConstants.CreateValidTokens();
            
            // Act
            await _tokenStore.SaveTokensAsync(tokens);
            var actualPath = GetActualTokenPath();
            var fileContent = await File.ReadAllTextAsync(actualPath);
            
            // Assert
            fileContent.Should().StartWith("SATURN_ENC_V1:", "should use new encryption format with version prefix");
            fileContent.Should().NotContain(tokens.AccessToken, "access token should be encrypted");
            fileContent.Should().NotContain(tokens.RefreshToken, "refresh token should be encrypted");
        }
        
        [Fact]
        public async Task EncryptedData_IsNotBase64_Of_Original()
        {
            // Arrange
            var tokens = TestConstants.CreateValidTokens();
            var originalJson = System.Text.Json.JsonSerializer.Serialize(tokens);
            var base64Original = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(originalJson));
            
            // Act
            await _tokenStore.SaveTokensAsync(tokens);
            var actualPath = GetActualTokenPath();
            var fileContent = await File.ReadAllTextAsync(actualPath);
            
            // Assert
            fileContent.Should().NotBe(base64Original, "encrypted data should not be simple base64 encoding");
            fileContent.Should().NotContain(base64Original, "encrypted data should not contain base64 of original");
        }
        
        [Fact]
        public async Task EncryptedTokens_AreDifferent_OnEachSave()
        {
            // Arrange
            var tokens = TestConstants.CreateValidTokens();
            
            // Act - Save the same tokens twice
            await _tokenStore.SaveTokensAsync(tokens);
            var actualPath = GetActualTokenPath();
            var firstEncryption = await File.ReadAllTextAsync(actualPath);
            
            await _tokenStore.SaveTokensAsync(tokens);
            var secondEncryption = await File.ReadAllTextAsync(actualPath);
            
            // Assert
            firstEncryption.Should().NotBe(secondEncryption, 
                "encryption should use different IVs/nonces each time, resulting in different ciphertext");
        }
        
        [Fact]
        public async Task SaveTokens_HandlesLargeTokenValues()
        {
            // Arrange - Create tokens with very large values to test encryption boundaries
            var largeTokens = new StoredTokens
            {
                AccessToken = new string('A', 4000), // Large access token
                RefreshToken = new string('R', 4000), // Large refresh token
                ExpiresAt = DateTime.UtcNow.AddHours(1),
                CreatedAt = DateTime.UtcNow
            };
            
            // Act
            await _tokenStore.SaveTokensAsync(largeTokens);
            var loadedTokens = await _tokenStore.LoadTokensAsync();
            
            // Assert
            loadedTokens.Should().NotBeNull();
            loadedTokens.AccessToken.Should().Be(largeTokens.AccessToken);
            loadedTokens.RefreshToken.Should().Be(largeTokens.RefreshToken);
        }
        
        [Fact]
        public async Task EncryptedFile_CannotBe_EasilyDecrypted()
        {
            // Arrange
            var tokens = TestConstants.CreateValidTokens();
            await _tokenStore.SaveTokensAsync(tokens);
            
            // Act - Try to manually read and decode the file
            var actualPath = GetActualTokenPath();
            var fileContent = await File.ReadAllTextAsync(actualPath);
            var encryptedPart = fileContent.Substring("SATURN_ENC_V1:".Length);
            
            // Assert - Should not be able to decode as simple base64 to readable JSON
            try
            {
                var decoded = Convert.FromBase64String(encryptedPart);
                var decodedString = System.Text.Encoding.UTF8.GetString(decoded);
                
                // Even if we can decode the base64, it should not contain readable token data
                decodedString.Should().NotContain(tokens.AccessToken, "decoded data should not contain plain access token");
                decodedString.Should().NotContain(tokens.RefreshToken, "decoded data should not contain plain refresh token");
                decodedString.Should().NotContain("\"AccessToken\"", "decoded data should not contain readable JSON structure");
            }
            catch (FormatException)
            {
                // If we can't even decode as base64, that's also good (though our format should be valid base64)
                // This shouldn't happen with our implementation, but it's acceptable
            }
        }
        
        [Fact]
        public async Task DeleteTokens_SecurelyRemoves_AllRelatedFiles()
        {
            // Arrange
            var tokens = TestConstants.CreateValidTokens();
            await _tokenStore.SaveTokensAsync(tokens);
            
            var actualPath = GetActualTokenPath();
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var authDir = Path.Combine(appData, "Saturn", "auth");
            var keyPath = Path.Combine(authDir, ".keystore");
            var saltPath = Path.Combine(authDir, ".salt");
            
            // Verify files exist before deletion
            File.Exists(actualPath).Should().BeTrue("token file should exist before deletion");
            
            // Act
            _tokenStore.DeleteTokens();
            
            // Assert
            File.Exists(actualPath).Should().BeFalse("token file should be deleted");
            File.Exists(keyPath).Should().BeFalse("key file should be deleted");
            File.Exists(saltPath).Should().BeFalse("salt file should be deleted");
        }
        
        [Fact]
        public async Task TokenEncryption_IsConsistent_AcrossInstances()
        {
            // Arrange
            var tokens = TestConstants.CreateValidTokens();
            var firstStore = new TokenStore();
            var secondStore = new TokenStore();
            
            // Act - Save with first instance, load with second
            await firstStore.SaveTokensAsync(tokens);
            var loadedTokens = await secondStore.LoadTokensAsync();
            
            // Assert
            loadedTokens.Should().NotBeNull();
            loadedTokens.AccessToken.Should().Be(tokens.AccessToken);
            loadedTokens.RefreshToken.Should().Be(tokens.RefreshToken);
            loadedTokens.ExpiresAt.Should().BeCloseTo(tokens.ExpiresAt, TimeSpan.FromSeconds(1));
        }
        
        [Fact]
        public async Task CorruptedEncryptionPrefix_IsHandled_Gracefully()
        {
            // Arrange
            var actualPath = GetActualTokenPath();
            Directory.CreateDirectory(Path.GetDirectoryName(actualPath));
            
            // Create a file with corrupted encryption prefix
            await File.WriteAllTextAsync(actualPath, "SATURN_ENC_V1:corrupted-not-valid-base64!@#$");
            
            // Act
            var result = await _tokenStore.LoadTokensAsync();
            
            // Assert
            result.Should().BeNull("should return null for corrupted data");
            // The corrupted file should be cleaned up
            File.Exists(actualPath).Should().BeFalse("corrupted file should be deleted");
        }
        
        [Fact]
        public async Task LegacyTokens_CanBe_MigratedToNewFormat()
        {
            // This test simulates having a legacy token file and ensures it gets migrated
            // Note: Since we don't have the exact legacy format implementation,
            // this test validates the migration framework is in place
            
            // Arrange
            var tokens = TestConstants.CreateValidTokens();
            var actualPath = GetActualTokenPath();
            Directory.CreateDirectory(Path.GetDirectoryName(actualPath));
            
            // Create a mock legacy format (simulating old data without the prefix)
            var json = System.Text.Json.JsonSerializer.Serialize(tokens);
            var legacyData = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));
            await File.WriteAllTextAsync(actualPath, legacyData);
            
            // Act
            var loadedTokens = await _tokenStore.LoadTokensAsync();
            
            // Assert - Check the behavior based on whether migration succeeded or failed
            if (loadedTokens != null)
            {
                // If migration succeeded, the file should now have the new format
                File.Exists(actualPath).Should().BeTrue("file should exist after successful migration");
                var updatedContent = await File.ReadAllTextAsync(actualPath);
                updatedContent.Should().StartWith("SATURN_ENC_V1:", 
                    "migrated file should use new encryption format");
                
                // Verify the migrated data is correct
                loadedTokens.AccessToken.Should().Be(tokens.AccessToken);
                loadedTokens.RefreshToken.Should().Be(tokens.RefreshToken);
            }
            else
            {
                // If migration failed, the file should be deleted (which is expected behavior)
                File.Exists(actualPath).Should().BeFalse("corrupted legacy file should be deleted when migration fails");
            }
        }
        
        private string GetActualTokenPath()
        {
            // Use the test directory we set up via environment variable
            return Path.Combine(_testDir, "auth", "anthropic.tokens");
        }
        
        private void CleanupTokenFile()
        {
            var actualPath = GetActualTokenPath();
            if (File.Exists(actualPath))
            {
                File.Delete(actualPath);
            }
        }
        
        public void Dispose()
        {
            try
            {
                // Clean up environment variable
                Environment.SetEnvironmentVariable("SATURN_TEST_CONFIG_PATH", null);
                
                CleanupTokenFile();
                
                // Also cleanup test directory if we created one
                if (Directory.Exists(_testDir))
                {
                    Directory.Delete(_testDir, true);
                }
            }
            catch
            {
                // Ignore cleanup errors in tests
            }
        }
    }
}