using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Saturn.Configuration
{
    /// <summary>
    /// Encrypts secret settings before they are persisted to disk. Uses DPAPI (current user)
    /// on Windows and AES-GCM with a per-user key file elsewhere. Values that cannot be
    /// decrypted (e.g. config copied from another user/machine) resolve to null so callers
    /// fall back to environment variables instead of using ciphertext as a credential.
    /// </summary>
    internal static class SecretProtector
    {
        private const string Prefix = "enc:v1:";
        private const string KeyFileName = "secret.key";
        private static readonly object KeyLock = new();
        private static readonly Dictionary<string, byte[]> KeyCache = new(StringComparer.OrdinalIgnoreCase);

        public static bool IsProtected(string? value) =>
            value != null && value.StartsWith(Prefix, StringComparison.Ordinal);

        public static string Protect(string plaintext, string appDataPath)
        {
            var data = Encoding.UTF8.GetBytes(plaintext);
            byte[] protectedBytes;

            if (OperatingSystem.IsWindows())
            {
                protectedBytes = ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
            }
            else
            {
                protectedBytes = AesGcmEncrypt(data, GetOrCreateKey(appDataPath));
            }

            return Prefix + Convert.ToBase64String(protectedBytes);
        }

        public static string? Unprotect(string? value, string appDataPath)
        {
            if (!IsProtected(value))
            {
                return value;
            }

            try
            {
                var bytes = Convert.FromBase64String(value!.Substring(Prefix.Length));

                var plaintext = OperatingSystem.IsWindows()
                    ? ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser)
                    : AesGcmDecrypt(bytes, GetOrCreateKey(appDataPath));

                return Encoding.UTF8.GetString(plaintext);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static byte[] AesGcmEncrypt(byte[] plaintext, byte[] key)
        {
            var nonce = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize);
            var tag = new byte[AesGcm.TagByteSizes.MaxSize];
            var ciphertext = new byte[plaintext.Length];

            using var aes = new AesGcm(key, tag.Length);
            aes.Encrypt(nonce, plaintext, ciphertext, tag);

            var result = new byte[nonce.Length + tag.Length + ciphertext.Length];
            nonce.CopyTo(result, 0);
            tag.CopyTo(result, nonce.Length);
            ciphertext.CopyTo(result, nonce.Length + tag.Length);
            return result;
        }

        private static byte[] AesGcmDecrypt(byte[] payload, byte[] key)
        {
            var nonceLength = AesGcm.NonceByteSizes.MaxSize;
            var tagLength = AesGcm.TagByteSizes.MaxSize;
            if (payload.Length < nonceLength + tagLength)
            {
                throw new CryptographicException("Protected payload is truncated.");
            }

            var nonce = payload.AsSpan(0, nonceLength);
            var tag = payload.AsSpan(nonceLength, tagLength);
            var ciphertext = payload.AsSpan(nonceLength + tagLength);
            var plaintext = new byte[ciphertext.Length];

            using var aes = new AesGcm(key, tagLength);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
            return plaintext;
        }

        private static byte[] GetOrCreateKey(string appDataPath)
        {
            lock (KeyLock)
            {
                if (KeyCache.TryGetValue(appDataPath, out var cached))
                {
                    return cached;
                }

                var keyPath = Path.Combine(appDataPath, KeyFileName);
                if (File.Exists(keyPath))
                {
                    var existing = File.ReadAllBytes(keyPath);
                    if (existing.Length == 32)
                    {
                        KeyCache[appDataPath] = existing;
                        return existing;
                    }
                }

                Directory.CreateDirectory(appDataPath);
                var key = RandomNumberGenerator.GetBytes(32);
                try
                {
                    // CreateNew is atomic across processes; if another instance won the
                    // race, use its key so already-encrypted secrets stay readable.
                    using var stream = new FileStream(keyPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    stream.Write(key, 0, key.Length);
                }
                catch (IOException)
                {
                    var winner = File.ReadAllBytes(keyPath);
                    if (winner.Length == 32)
                    {
                        key = winner;
                    }
                }

                if (!OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(keyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }

                KeyCache[appDataPath] = key;
                return key;
            }
        }
    }
}
