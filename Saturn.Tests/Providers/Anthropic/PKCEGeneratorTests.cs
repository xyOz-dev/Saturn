using Xunit;
using FluentAssertions;
using SaturnFork.Providers.Anthropic.Utils;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System;
using System.Collections.Generic;

namespace Saturn.Tests.Providers.Anthropic
{
    public class PKCEGeneratorTests
    {
        [Fact]
        public void Generate_CreatesValidPKCEPair()
        {
            // Act
            var pkce = PKCEGenerator.Generate();
            
            // Assert
            pkce.Should().NotBeNull();
            pkce.Verifier.Should().NotBeNullOrEmpty();
            pkce.Challenge.Should().NotBeNullOrEmpty();
        }
        
        [Fact]
        public void Generate_VerifierHasCorrectLength()
        {
            // Act
            var pkce = PKCEGenerator.Generate();
            
            // Assert
            pkce.Verifier.Length.Should().Be(128);
        }
        
        [Fact]
        public void Generate_VerifierUsesValidCharacters()
        {
            // Arrange
            var validPattern = @"^[A-Za-z0-9\-._~]+$";
            
            // Act
            var pkce = PKCEGenerator.Generate();
            
            // Assert
            Regex.IsMatch(pkce.Verifier, validPattern).Should().BeTrue();
        }
        
        [Fact]
        public void Generate_ChallengeIsBase64Url()
        {
            // Act
            var pkce = PKCEGenerator.Generate();
            
            // Assert
            pkce.Challenge.Should().NotContain("+");
            pkce.Challenge.Should().NotContain("/");
            pkce.Challenge.Should().NotContain("=");
        }
        
        [Fact]
        public void Generate_ChallengeIsValidBase64UrlLength()
        {
            // Act
            var pkce = PKCEGenerator.Generate();
            
            // Assert - SHA256 hash base64url encoded should be 43 characters
            pkce.Challenge.Length.Should().Be(43);
        }
        
        [Fact]
        public void Generate_ProducesUniqueValues()
        {
            // Act
            var pkce1 = PKCEGenerator.Generate();
            var pkce2 = PKCEGenerator.Generate();
            
            // Assert
            pkce1.Verifier.Should().NotBe(pkce2.Verifier);
            pkce1.Challenge.Should().NotBe(pkce2.Challenge);
        }
        
        [Theory]
        [InlineData(100)]
        public void Generate_Performance_Under50ms(int iterations)
        {
            // Arrange & Act
            var stopwatch = Stopwatch.StartNew();
            
            for (int i = 0; i < iterations; i++)
            {
                PKCEGenerator.Generate();
            }
            
            stopwatch.Stop();
            
            // Assert
            var avgMs = stopwatch.ElapsedMilliseconds / (double)iterations;
            avgMs.Should().BeLessThan(50, "PKCE generation should be fast");
        }
        
        [Fact]
        public void Generate_MultipleGenerations_AllUnique()
        {
            // Arrange
            const int count = 1000;
            var verifiers = new HashSet<string>();
            var challenges = new HashSet<string>();
            
            // Act
            for (int i = 0; i < count; i++)
            {
                var pkce = PKCEGenerator.Generate();
                verifiers.Add(pkce.Verifier);
                challenges.Add(pkce.Challenge);
            }
            
            // Assert
            verifiers.Should().HaveCount(count, "all verifiers should be unique");
            challenges.Should().HaveCount(count, "all challenges should be unique");
        }
        
        [Fact]
        public void Generate_VerifierMatchesOAuthSpec()
        {
            // Act
            var pkce = PKCEGenerator.Generate();
            
            // Assert
            // OAuth 2.0 PKCE spec requires 43-128 characters from unreserved character set
            pkce.Verifier.Length.Should().BeGreaterOrEqualTo(43)
                .And.BeLessOrEqualTo(128);
            
            // Unreserved characters: A-Z, a-z, 0-9, -, ., _, ~
            var unreservedPattern = @"^[A-Za-z0-9\-._~]+$";
            pkce.Verifier.Should().MatchRegex(unreservedPattern);
        }
        
        [Fact]
        public void Generate_ChallengeMatchesOAuthSpec()
        {
            // Act  
            var pkce = PKCEGenerator.Generate();
            
            // Assert
            // Challenge should be base64url-encoded SHA256 hash
            // Base64url uses A-Z, a-z, 0-9, -, _ and no padding
            var base64UrlPattern = @"^[A-Za-z0-9\-_]+$";
            pkce.Challenge.Should().MatchRegex(base64UrlPattern);
            
            // SHA256 hash encoded as base64url should be exactly 43 characters
            pkce.Challenge.Length.Should().Be(43);
        }
        
        [Fact]
        public void PKCEPair_PropertiesAreNotNull()
        {
            // Act
            var pkce = PKCEGenerator.Generate();
            
            // Assert
            pkce.Verifier.Should().NotBeNull();
            pkce.Challenge.Should().NotBeNull();
        }
    }
}