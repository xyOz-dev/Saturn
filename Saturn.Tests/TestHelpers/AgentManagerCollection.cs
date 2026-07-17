using Xunit;

namespace Saturn.Tests.TestHelpers
{
    /// <summary>
    /// Serializes test classes that mutate the process-wide AgentManager singleton
    /// (client source, parent session, agent registry), which would otherwise race
    /// under xUnit's parallel runner.
    /// </summary>
    [CollectionDefinition("AgentManager")]
    public class AgentManagerCollection
    {
    }
}
