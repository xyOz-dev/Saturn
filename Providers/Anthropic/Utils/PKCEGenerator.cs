using System;
using System.Security.Cryptography;
using System.Text;

namespace Saturn.Providers.Anthropic.Utils
{
    public class PKCEGenerator
    {
        public class PKCEPair
        {
            public string Verifier { get; set; }
            public string Challenge { get; set; }
        }
        
        public static PKCEPair Generate()
        {
            return Generate(128); // Use default length
        }
        
        public static PKCEPair Generate(int verifierLength)
        {
            // Validate verifier length according to RFC 7636
            if (verifierLength < 43 || verifierLength > 128)
                throw new ArgumentException("PKCE verifier length must be between 43 and 128 characters", nameof(verifierLength));
            
            // Generate random verifier
            var verifier = GenerateRandomString(verifierLength);
            
            if (string.IsNullOrEmpty(verifier))
                throw new InvalidOperationException("Failed to generate PKCE verifier");
            
            // Generate challenge from verifier using SHA256
            var challenge = GenerateChallenge(verifier);
            
            if (string.IsNullOrEmpty(challenge))
                throw new InvalidOperationException("Failed to generate PKCE challenge");
            
            var result = new PKCEPair
            {
                Verifier = verifier,
                Challenge = challenge
            };
            
            // Validate the generated pair
            ValidatePKCEPair(result);
            
            return result;
        }
        
        private static string GenerateRandomString(int length)
        {
            if (length <= 0)
                throw new ArgumentException("Length must be greater than 0", nameof(length));
            
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~";
            var random = new byte[length];
            
            using (var rng = RandomNumberGenerator.Create())
            {
                if (rng == null)
                    throw new InvalidOperationException("Failed to create random number generator");
                
                rng.GetBytes(random);
            }
            
            var result = new StringBuilder(length);
            foreach (byte b in random)
            {
                result.Append(chars[b % chars.Length]);
            }
            
            var finalResult = result.ToString();
            
            // Validate the result contains only valid characters
            if (!System.Text.RegularExpressions.Regex.IsMatch(finalResult, @"^[A-Za-z0-9\-\._~]+$"))
                throw new InvalidOperationException("Generated string contains invalid characters");
            
            return finalResult;
        }
        
        private static string GenerateChallenge(string verifier)
        {
            if (string.IsNullOrEmpty(verifier))
                throw new ArgumentException("Verifier cannot be null or empty", nameof(verifier));
            
            if (verifier.Length < 43 || verifier.Length > 128)
                throw new ArgumentException("Verifier length must be between 43 and 128 characters", nameof(verifier));
            
            using (var sha256 = SHA256.Create())
            {
                if (sha256 == null)
                    throw new InvalidOperationException("Failed to create SHA256 hasher");
                
                var bytes = Encoding.UTF8.GetBytes(verifier);
                var hash = sha256.ComputeHash(bytes);
                
                if (hash == null || hash.Length == 0)
                    throw new InvalidOperationException("Failed to compute SHA256 hash");
                
                // Convert to base64url (URL-safe base64)
                var base64 = Convert.ToBase64String(hash);
                var challenge = base64
                    .Replace('+', '-')
                    .Replace('/', '_')
                    .TrimEnd('=');
                
                // Validate the generated challenge
                if (string.IsNullOrEmpty(challenge))
                    throw new InvalidOperationException("Generated challenge is empty");
                
                if (!System.Text.RegularExpressions.Regex.IsMatch(challenge, @"^[A-Za-z0-9\-_]+$"))
                    throw new InvalidOperationException("Generated challenge contains invalid characters");
                
                return challenge;
            }
        }
        
        private static void ValidatePKCEPair(PKCEPair pair)
        {
            if (pair == null)
                throw new ArgumentNullException(nameof(pair));
            
            if (string.IsNullOrEmpty(pair.Verifier))
                throw new InvalidOperationException("PKCE pair verifier is null or empty");
            
            if (string.IsNullOrEmpty(pair.Challenge))
                throw new InvalidOperationException("PKCE pair challenge is null or empty");
            
            if (pair.Verifier.Length < 43 || pair.Verifier.Length > 128)
                throw new InvalidOperationException("PKCE verifier length is invalid");
            
            // Validate verifier contains only valid characters
            if (!System.Text.RegularExpressions.Regex.IsMatch(pair.Verifier, @"^[A-Za-z0-9\-\._~]+$"))
                throw new InvalidOperationException("PKCE verifier contains invalid characters");
            
            // Validate challenge contains only valid characters
            if (!System.Text.RegularExpressions.Regex.IsMatch(pair.Challenge, @"^[A-Za-z0-9\-_]+$"))
                throw new InvalidOperationException("PKCE challenge contains invalid characters");
        }
    }
}