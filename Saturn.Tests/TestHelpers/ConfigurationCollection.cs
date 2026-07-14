using Xunit;

namespace Saturn.Tests.TestHelpers
{
    /// <summary>
    /// Serializes test classes that manipulate the process-wide SATURN_CONFIG_DIR and provider
    /// API-key environment variables, which would otherwise race under xUnit's parallel runner.
    /// </summary>
    [CollectionDefinition("Configuration")]
    public class ConfigurationCollection
    {
    }
}
