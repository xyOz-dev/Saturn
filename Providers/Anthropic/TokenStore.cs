using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Saturn.Providers.Anthropic.Models;

namespace Saturn.Providers.Anthropic
{
    public class TokenStore
    {
        private readonly string _tokenPath;
        private readonly string _keyPath;
        private readonly string _saltPath;
        private const int KeySize = 256 / 8; // 256-bit key
        private const int IvSize = 128 / 8;  // 128-bit IV
        private const int SaltSize = 16;     // 128-bit salt
        private const string EncryptionPrefix = "SATURN_ENC_V1:"; // Version prefix for future upgrades
        
        public TokenStore()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var saturnDir = Path.Combine(appData, "Saturn", "auth");
            Directory.CreateDirectory(saturnDir);
            _tokenPath = Path.Combine(saturnDir, "anthropic.tokens");
            _keyPath = Path.Combine(saturnDir, ".keystore");
            _saltPath = Path.Combine(saturnDir, ".salt");
        }
        
        public async Task SaveTokensAsync(StoredTokens tokens)
        {
            // Validate input parameters
            if (tokens == null)
                throw new ArgumentNullException(nameof(tokens));
            
            if (string.IsNullOrEmpty(tokens.AccessToken))
                throw new ArgumentException("Access token cannot be null or empty", nameof(tokens));
            
            if (string.IsNullOrWhiteSpace(tokens.AccessToken))
                throw new ArgumentException("Access token cannot be whitespace only", nameof(tokens));
            
            if (tokens.ExpiresAt <= DateTime.UtcNow)
                throw new ArgumentException("Token expiration time cannot be in the past", nameof(tokens));
            
            if (tokens.CreatedAt > DateTime.UtcNow.AddMinutes(5))
                throw new ArgumentException("Token creation time cannot be in the future", nameof(tokens));
            
            // Ensure the directory exists before writing the file
            var directory = Path.GetDirectoryName(_tokenPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            var json = JsonSerializer.Serialize(tokens);
            
            if (string.IsNullOrEmpty(json))
                throw new InvalidOperationException("Failed to serialize tokens to JSON");
            
            var encrypted = await EncryptAsync(json);
            
            if (string.IsNullOrEmpty(encrypted))
                throw new InvalidOperationException("Failed to encrypt token data");
            
            await File.WriteAllTextAsync(_tokenPath, encrypted);
        }
        
        public async Task<StoredTokens> LoadTokensAsync()
        {
            if (!File.Exists(_tokenPath))
                return null;
                
            var encrypted = await File.ReadAllTextAsync(_tokenPath);
            
            try
            {
                var json = await DecryptAsync(encrypted);
                return JsonSerializer.Deserialize<StoredTokens>(json);
            }
            catch (Exception ex)
            {
                // Log the exception type for debugging but still handle gracefully
                System.Diagnostics.Debug.WriteLine($"Token decryption failed: {ex.GetType().Name}");
                
                // If decryption fails, attempt migration or delete corrupted file
                return await HandleDecryptionFailure(encrypted);
            }
        }
        
        public void DeleteTokens()
        {
            try
            {
                if (File.Exists(_tokenPath))
                {
                    // Securely overwrite file before deletion
                    SecureDelete(_tokenPath);
                }
                
                if (File.Exists(_keyPath))
                {
                    SecureDelete(_keyPath);
                }
                
                if (File.Exists(_saltPath))
                {
                    SecureDelete(_saltPath);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Secure deletion failed: {ex.Message}");
                // Fallback to regular deletion
                try
                {
                    File.Delete(_tokenPath);
                    File.Delete(_keyPath);
                    File.Delete(_saltPath);
                }
                catch
                {
                    // Ignore final cleanup errors
                }
            }
        }
        
        private async Task<string> EncryptAsync(string plainText)
        {
            if (IsWindows())
            {
                return await EncryptWithDpapiAsync(plainText);
            }
            else
            {
                return await EncryptWithAesAsync(plainText);
            }
        }
        
        private async Task<string> DecryptAsync(string cipherText)
        {
            // Check if this is our new encrypted format
            if (cipherText.StartsWith(EncryptionPrefix))
            {
                var encryptedData = cipherText.Substring(EncryptionPrefix.Length);
                
                if (IsWindows())
                {
                    return await DecryptWithDpapiAsync(encryptedData);
                }
                else
                {
                    return await DecryptWithAesAsync(encryptedData);
                }
            }
            else
            {
                // Try to handle legacy format (might be old base64 or previous encryption)
                return await HandleLegacyFormat(cipherText);
            }
        }
        
        private async Task<string> EncryptWithDpapiAsync(string plainText)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var plainBytes = Encoding.UTF8.GetBytes(plainText);
                    var encryptedBytes = ProtectedData.Protect(
                        plainBytes, 
                        null, 
                        DataProtectionScope.CurrentUser);
                    return EncryptionPrefix + Convert.ToBase64String(encryptedBytes);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("DPAPI encryption failed", ex);
                }
            });
        }
        
        private async Task<string> DecryptWithDpapiAsync(string encryptedData)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var encryptedBytes = Convert.FromBase64String(encryptedData);
                    var plainBytes = ProtectedData.Unprotect(
                        encryptedBytes, 
                        null, 
                        DataProtectionScope.CurrentUser);
                    return Encoding.UTF8.GetString(plainBytes);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("DPAPI decryption failed", ex);
                }
            });
        }
        
        private async Task<string> EncryptWithAesAsync(string plainText)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var plainBytes = Encoding.UTF8.GetBytes(plainText);
                    var key = GetOrCreateSecureKey();
                    
                    using (var aes = Aes.Create())
                    {
                        aes.KeySize = 256;
                        aes.BlockSize = 128;
                        aes.Mode = CipherMode.CBC;
                        aes.Padding = PaddingMode.PKCS7;
                        aes.Key = key;
                        aes.GenerateIV();
                        
                        using (var encryptor = aes.CreateEncryptor())
                        {
                            var encrypted = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
                            
                            // Combine IV + encrypted data
                            var result = new byte[aes.IV.Length + encrypted.Length];
                            Array.Copy(aes.IV, 0, result, 0, aes.IV.Length);
                            Array.Copy(encrypted, 0, result, aes.IV.Length, encrypted.Length);
                            
                            return EncryptionPrefix + Convert.ToBase64String(result);
                        }
                    }
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("AES encryption failed", ex);
                }
            });
        }
        
        private async Task<string> DecryptWithAesAsync(string encryptedData)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var cipherBytes = Convert.FromBase64String(encryptedData);
                    var key = GetOrCreateSecureKey();
                    
                    if (cipherBytes.Length < IvSize)
                    {
                        throw new InvalidOperationException("Invalid encrypted data format");
                    }
                    
                    using (var aes = Aes.Create())
                    {
                        aes.KeySize = 256;
                        aes.BlockSize = 128;
                        aes.Mode = CipherMode.CBC;
                        aes.Padding = PaddingMode.PKCS7;
                        aes.Key = key;
                        
                        // Extract IV and encrypted data
                        var iv = new byte[IvSize];
                        var encrypted = new byte[cipherBytes.Length - IvSize];
                        Array.Copy(cipherBytes, 0, iv, 0, IvSize);
                        Array.Copy(cipherBytes, IvSize, encrypted, 0, encrypted.Length);
                        
                        aes.IV = iv;
                        
                        using (var decryptor = aes.CreateDecryptor())
                        {
                            var plainBytes = decryptor.TransformFinalBlock(encrypted, 0, encrypted.Length);
                            return Encoding.UTF8.GetString(plainBytes);
                        }
                    }
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("AES decryption failed", ex);
                }
            });
        }
        
        private byte[] GetOrCreateSecureKey()
        {
            var salt = GetOrCreateSalt();
            
            // Use PBKDF2 to derive key from user-specific data + salt
            var userSpecificData = GetUserSpecificData();
            
            using (var pbkdf2 = new Rfc2898DeriveBytes(userSpecificData, salt, 100000, HashAlgorithmName.SHA256))
            {
                return pbkdf2.GetBytes(KeySize);
            }
        }
        
        private byte[] GetOrCreateSalt()
        {
            if (File.Exists(_saltPath))
            {
                try
                {
                    var saltData = File.ReadAllBytes(_saltPath);
                    if (saltData.Length == SaltSize)
                    {
                        return saltData;
                    }
                }
                catch
                {
                    // If we can't read the salt, create a new one
                }
            }
            
            // Generate new salt
            var salt = new byte[SaltSize];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(salt);
            }
            
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_saltPath));
                File.WriteAllBytes(_saltPath, salt);
                
                // Set restrictive permissions if possible
                SetFilePermissions(_saltPath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save salt: {ex.Message}");
                // Continue with in-memory salt (less secure but functional)
            }
            
            return salt;
        }
        
        private string GetUserSpecificData()
        {
            // Create a user-specific string that includes multiple identifiers
            var userInfo = Environment.UserName ?? "unknown";
            var machineInfo = Environment.MachineName ?? "unknown";
            var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) ?? "unknown";
            
            // Combine these with a fixed string to create deterministic but user-specific data
            return $"Saturn-{userInfo}-{machineInfo}-{homeDir.GetHashCode()}";
        }
        
        private async Task<StoredTokens> HandleDecryptionFailure(string encryptedData)
        {
            try
            {
                // Try to handle as legacy format (old implementation)
                var result = await TryMigrateLegacyTokens(encryptedData);
                if (result != null)
                {
                    return result;
                }
            }
            catch
            {
                // Migration failed
            }
            
            // If we get here, either migration failed or returned null
            // Delete corrupted file
            try
            {
                File.Delete(_tokenPath);
            }
            catch
            {
                // Ignore deletion errors
            }
            return null;
        }
        
        private async Task<string> HandleLegacyFormat(string data)
        {
            // This handles the old format that might exist
            // First try as if it's the old DPAPI format on Windows
            if (IsWindows())
            {
                try
                {
                    var encryptedBytes = Convert.FromBase64String(data);
                    var plainBytes = ProtectedData.Unprotect(
                        encryptedBytes, 
                        null, 
                        DataProtectionScope.CurrentUser);
                    return Encoding.UTF8.GetString(plainBytes);
                }
                catch
                {
                    // Fall through to try other formats
                }
            }
            
            // Try as the old AES format
            try
            {
                var cipherBytes = Convert.FromBase64String(data);
                var legacyKey = GetLegacyKey();
                
                if (legacyKey != null && cipherBytes.Length > 16)
                {
                    using (var aes = Aes.Create())
                    {
                        aes.Key = legacyKey;
                        
                        var iv = new byte[16];
                        var encrypted = new byte[cipherBytes.Length - 16];
                        Array.Copy(cipherBytes, 0, iv, 0, 16);
                        Array.Copy(cipherBytes, 16, encrypted, 0, encrypted.Length);
                        
                        aes.IV = iv;
                        
                        using (var decryptor = aes.CreateDecryptor())
                        {
                            var plainBytes = decryptor.TransformFinalBlock(encrypted, 0, encrypted.Length);
                            return Encoding.UTF8.GetString(plainBytes);
                        }
                    }
                }
            }
            catch
            {
                // Fall through
            }
            
            throw new InvalidOperationException("Unable to decrypt legacy format");
        }
        
        private async Task<StoredTokens> TryMigrateLegacyTokens(string legacyData)
        {
            try
            {
                // Try to decrypt using legacy method
                var json = await HandleLegacyFormat(legacyData);
                var tokens = JsonSerializer.Deserialize<StoredTokens>(json);
                
                if (tokens != null)
                {
                    // Successfully decrypted legacy format, re-save with new encryption
                    await SaveTokensAsync(tokens);
                    return tokens;
                }
            }
            catch
            {
                // Migration failed
            }
            
            return null;
        }
        
        private byte[] GetLegacyKey()
        {
            var legacyKeyPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Saturn", "auth", ".key");
                
            if (File.Exists(legacyKeyPath))
            {
                try
                {
                    return Convert.FromBase64String(File.ReadAllText(legacyKeyPath));
                }
                catch
                {
                    return null;
                }
            }
            
            return null;
        }
        
        private void SecureDelete(string filePath)
        {
            if (!File.Exists(filePath)) return;
            
            try
            {
                // Get file info
                var fileInfo = new FileInfo(filePath);
                var fileSize = fileInfo.Length;
                
                // Overwrite file with random data multiple times
                using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Write))
                {
                    var buffer = new byte[4096];
                    using (var rng = RandomNumberGenerator.Create())
                    {
                        for (int pass = 0; pass < 3; pass++)
                        {
                            fileStream.Seek(0, SeekOrigin.Begin);
                            long bytesLeft = fileSize;
                            
                            while (bytesLeft > 0)
                            {
                                var bytesToWrite = (int)Math.Min(buffer.Length, bytesLeft);
                                rng.GetBytes(buffer, 0, bytesToWrite);
                                fileStream.Write(buffer, 0, bytesToWrite);
                                bytesLeft -= bytesToWrite;
                            }
                            
                            fileStream.Flush();
                        }
                    }
                }
                
                // Finally delete the file
                File.Delete(filePath);
            }
            catch
            {
                // If secure deletion fails, fall back to regular deletion
                try
                {
                    File.Delete(filePath);
                }
                catch
                {
                    // Ignore final deletion errors
                }
            }
        }
        
        private void SetFilePermissions(string filePath)
        {
            try
            {
                if (IsWindows())
                {
                    // On Windows, the file is already protected by being in user's AppData
                    // Additional ACL modifications could be added here if needed
                }
                else
                {
                    // On Unix-like systems, set permissions to 600 (owner read/write only)
                    var process = new System.Diagnostics.Process
                    {
                        StartInfo = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "chmod",
                            Arguments = $"600 \"{filePath}\"",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        }
                    };
                    process.Start();
                    process.WaitForExit(5000); // Wait up to 5 seconds
                }
            }
            catch
            {
                // Ignore permission setting errors
                System.Diagnostics.Debug.WriteLine($"Could not set restrictive permissions on {filePath}");
            }
        }
        
        private static bool IsWindows()
        {
            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        }
    }
}