using Saturn.Agents.Core;

namespace Saturn.Tools.Core
{
    public static class AgentContext
    {
        private static AgentConfiguration _currentConfiguration = null!;
        
        public static AgentConfiguration CurrentConfiguration
        {
            get => _currentConfiguration;
            set => _currentConfiguration = value;
        }
        
        public static bool RequireCommandApproval => _currentConfiguration?.RequireCommandApproval ?? true;
    }
}