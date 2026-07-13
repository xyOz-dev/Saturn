using System.Threading.Tasks;
using FluentAssertions;
using Saturn.Core.Approvals;
using Xunit;

namespace Saturn.Tests.Approvals
{
    public class CommandJudgeTests
    {
        private static JudgeRequest Request(string command = "dotnet build") =>
            new(command, "/repo", "TestAgent", AgentPurpose: null, TaskDescription: null);

        private static (CommandJudge Judge, FakeClientSource Source) CreateJudge(string response)
        {
            var source = new FakeClientSource();
            source.Client.ResponseContent = response;
            return (new CommandJudge(source, () => "judge-model"), source);
        }

        [Theory]
        [InlineData("APPROVE: routine", JudgeDecision.Approve, "routine")]
        [InlineData("deny: force push", JudgeDecision.Deny, "force push")]
        [InlineData("ESCALATE: not sure", JudgeDecision.Escalate, "not sure")]
        public async Task JudgeAsync_ParsesEachVerdictShape(string response, JudgeDecision expected, string reason)
        {
            var (judge, _) = CreateJudge(response);

            var verdict = await judge.JudgeAsync(Request());

            verdict.Decision.Should().Be(expected);
            verdict.Reason.Should().Be(reason);
        }

        [Fact]
        public async Task JudgeAsync_VerdictOnLaterLine_IsStillFound()
        {
            var (judge, _) = CreateJudge("Thinking about it...\nAPPROVE: safe build step");

            var verdict = await judge.JudgeAsync(Request());

            verdict.Decision.Should().Be(JudgeDecision.Approve);
        }

        [Fact]
        public async Task JudgeAsync_UnparseableOutput_Escalates()
        {
            var (judge, _) = CreateJudge("sounds good to me");

            var verdict = await judge.JudgeAsync(Request());

            verdict.Decision.Should().Be(JudgeDecision.Escalate);
        }

        [Fact]
        public async Task JudgeAsync_EmptyOutput_Escalates()
        {
            var (judge, _) = CreateJudge("");

            var verdict = await judge.JudgeAsync(Request());

            verdict.Decision.Should().Be(JudgeDecision.Escalate);
        }

        [Fact]
        public async Task JudgeAsync_UsesConfiguredModelAndZeroTemperature()
        {
            var (judge, source) = CreateJudge("APPROVE: ok");

            await judge.JudgeAsync(Request("git status"));

            var request = source.Client.LastRequest;
            request.Should().NotBeNull();
            request!.Model.Should().Be("judge-model");
            request.Temperature.Should().Be(0);
        }
    }
}
