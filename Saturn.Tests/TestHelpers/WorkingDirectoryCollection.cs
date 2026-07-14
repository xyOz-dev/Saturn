using Xunit;

namespace Saturn.Tests.TestHelpers
{
    /// <summary>
    /// Serializes test classes that change the process-wide current working directory,
    /// which would otherwise race when xUnit runs test classes in parallel.
    /// </summary>
    [CollectionDefinition("WorkingDirectory")]
    public class WorkingDirectoryCollection
    {
    }
}
