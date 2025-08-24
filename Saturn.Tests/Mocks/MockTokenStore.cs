using System;
using System.Threading.Tasks;
using SaturnFork.Providers.Anthropic.Models;

namespace Saturn.Tests.Mocks
{
    public class MockTokenStore
    {
        public StoredTokens? StoredTokens { get; set; }
        public Exception? ExceptionToThrow { get; set; }
        public bool DeleteTokensCalled { get; private set; }
        public bool SaveTokensCalled { get; private set; }
        public bool LoadTokensCalled { get; private set; }
        public StoredTokens? LastSavedTokens { get; private set; }
        
        public Task SaveTokensAsync(StoredTokens tokens)
        {
            SaveTokensCalled = true;
            LastSavedTokens = tokens;
            
            if (ExceptionToThrow != null)
            {
                throw ExceptionToThrow;
            }
            
            StoredTokens = tokens;
            return Task.CompletedTask;
        }
        
        public Task<StoredTokens?> LoadTokensAsync()
        {
            LoadTokensCalled = true;
            
            if (ExceptionToThrow != null)
            {
                throw ExceptionToThrow;
            }
            
            return Task.FromResult(StoredTokens);
        }
        
        public void DeleteTokens()
        {
            DeleteTokensCalled = true;
            StoredTokens = null;
            
            if (ExceptionToThrow != null)
            {
                throw ExceptionToThrow;
            }
        }
        
        public void Reset()
        {
            StoredTokens = null;
            ExceptionToThrow = null;
            DeleteTokensCalled = false;
            SaveTokensCalled = false;
            LoadTokensCalled = false;
            LastSavedTokens = null;
        }
    }
}