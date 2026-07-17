using System.Linq;
using FluentAssertions;
using Saturn.Agents.MultiAgent;
using Xunit;

namespace Saturn.Tests.Agents
{
    public class AgentTypeRegistryTests
    {
        [Fact]
        public void TryGet_IsCaseInsensitive()
        {
            AgentTypeRegistry.TryGet("Explorer", out var type).Should().BeTrue();
            type.Name.Should().Be("explorer");
        }

        [Fact]
        public void TryGet_UnknownType_ReturnsFalse()
        {
            AgentTypeRegistry.TryGet("warlock", out _).Should().BeFalse();
        }

        [Fact]
        public void Default_IsGeneralWithFullToolAccess()
        {
            AgentTypeRegistry.Default.Name.Should().Be("general");
            AgentTypeRegistry.Default.ToolNames.Should().BeNull();
        }

        [Fact]
        public void ReadOnlyTypes_DoNotIncludeWriteTools()
        {
            var writeTools = new[] { "apply_diff", "write_file", "search_and_replace", "delete_file" };

            foreach (var typeName in new[] { "explorer", "reviewer" })
            {
                AgentTypeRegistry.TryGet(typeName, out var type).Should().BeTrue();
                type.ToolNames.Should().NotBeNull();
                type.ToolNames.Should().NotContain(writeTools);
            }
        }

        [Fact]
        public void DescribeAll_ListsEveryType()
        {
            var described = AgentTypeRegistry.DescribeAll("   ");

            foreach (var type in AgentTypeRegistry.All)
            {
                described.Should().Contain($"   - {type.Name}: ");
            }
        }

        [Fact]
        public void Names_MatchRegistry()
        {
            AgentTypeRegistry.Names.Should().BeEquivalentTo(
                AgentTypeRegistry.All.Select(t => t.Name));
        }
    }
}
