using System.Threading;
using Saturn.Agents.Core;

namespace Saturn.Tools.Core
{
    public sealed class AgentExecutionContext
    {
        public required AgentConfiguration Configuration { get; init; }
        public string AgentInstanceId { get; init; } = "";
        public string? SessionId { get; init; }
        public string? ManagerAgentId { get; init; }
        public string AgentName { get; init; } = "";
        public bool IsOrchestrator { get; init; }

        /// <summary>The executing agent, for tools that need to inspect its chat history.</summary>
        public AgentBase? Agent { get; init; }
    }

    public static class AgentContext
    {
        private static readonly AsyncLocal<AgentExecutionContext?> _current = new();

        public static AgentExecutionContext? Current
        {
            get => _current.Value;
            set => _current.Value = value;
        }

        public static AgentConfiguration CurrentConfiguration
        {
            get => Current?.Configuration!;
            set => Current = value == null ? null : new AgentExecutionContext
            {
                Configuration = value,
                AgentName = value.Name
            };
        }

        public static bool RequireCommandApproval => Current?.Configuration?.RequireCommandApproval ?? true;

        public static bool IsSubAgent => Current?.ManagerAgentId != null;
    }
}
