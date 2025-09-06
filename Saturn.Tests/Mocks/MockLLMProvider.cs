using Saturn.Providers;
using System;
using System.Threading.Tasks;

namespace Saturn.Tests.Mocks
{
    public class MockLLMProvider : ILLMProvider
    {
        public string Name { get; set; } = "Mock Provider";
        public string Description { get; set; } = "Mock provider for testing";
        public AuthenticationType AuthType { get; set; } = AuthenticationType.None;
        public bool IsAuthenticated { get; set; } = true;
        
        public bool AuthenticateCalled { get; private set; }
        public bool LogoutCalled { get; private set; }
        public bool GetClientCalled { get; private set; }
        public bool SaveConfigurationCalled { get; private set; }
        public bool LoadConfigurationCalled { get; private set; }
        
        public Exception? ExceptionToThrow { get; set; }
        public bool AuthenticateResult { get; set; } = true;
        public ILLMClient? ClientToReturn { get; set; }
        
        public Task<bool> AuthenticateAsync()
        {
            AuthenticateCalled = true;
            
            if (ExceptionToThrow != null)
            {
                throw ExceptionToThrow;
            }
            
            IsAuthenticated = AuthenticateResult;
            return Task.FromResult(AuthenticateResult);
        }
        
        public Task<ILLMClient> GetClientAsync()
        {
            GetClientCalled = true;
            
            if (ExceptionToThrow != null)
            {
                throw ExceptionToThrow;
            }
            
            return Task.FromResult(ClientToReturn ?? new MockLLMClient());
        }
        
        public Task LoadConfigurationAsync()
        {
            LoadConfigurationCalled = true;
            
            if (ExceptionToThrow != null)
            {
                throw ExceptionToThrow;
            }
            
            return Task.CompletedTask;
        }
        
        public Task LogoutAsync()
        {
            LogoutCalled = true;
            IsAuthenticated = false;
            
            if (ExceptionToThrow != null)
            {
                throw ExceptionToThrow;
            }
            
            return Task.CompletedTask;
        }
        
        public Task SaveConfigurationAsync()
        {
            SaveConfigurationCalled = true;
            
            if (ExceptionToThrow != null)
            {
                throw ExceptionToThrow;
            }
            
            return Task.CompletedTask;
        }
        
        public void Reset()
        {
            AuthenticateCalled = false;
            LogoutCalled = false;
            GetClientCalled = false;
            SaveConfigurationCalled = false;
            LoadConfigurationCalled = false;
            ExceptionToThrow = null;
            AuthenticateResult = true;
            IsAuthenticated = true;
        }
    }
}