using FluentAssertions;
using Saturn.Agents.Core;
using Xunit;

namespace Saturn.Tests.Agents
{
    public class ModeTests
    {
        [Fact]
        public void BareConstructor_RequiresCommandApproval()
        {
            // A false default here silently disables the whole approval pipeline
            // for any mode built without CreateDefault.
            new Mode().RequireCommandApproval.Should().BeTrue();
        }

        [Fact]
        public void CreateDefault_RequiresCommandApproval()
        {
            Mode.CreateDefault().RequireCommandApproval.Should().BeTrue();
        }

        [Fact]
        public void Clone_PreservesApprovalSetting()
        {
            var mode = Mode.CreateDefault();
            mode.RequireCommandApproval = true;

            mode.Clone().RequireCommandApproval.Should().BeTrue();
        }
    }
}
