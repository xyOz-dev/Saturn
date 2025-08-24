using System.Threading.Tasks;

namespace Saturn.Providers
{
    public enum AuthenticationType
    {
        None,
        ApiKey,
        OAuth
    }
    
    public interface ILLMProvider
    {
        // Provider identification
        string Name { get; }
        string Description { get; }
        
        // Authentication
        AuthenticationType AuthType { get; }
        bool IsAuthenticated { get; }
        Task<bool> AuthenticateAsync();
        Task LogoutAsync();
        
        // Client management
        Task<ILLMClient> GetClientAsync();
        
        // Configuration
        Task SaveConfigurationAsync();
        Task LoadConfigurationAsync();
    }
}