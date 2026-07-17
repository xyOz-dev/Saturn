using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Saturn.Agents.MultiAgent;
using Saturn.OpenRouter.Models.Api.Chat;
using Saturn.Providers;
using Saturn.Tests.Approvals;
using Saturn.Tests.Providers;
using Saturn.Tools.MultiAgent;
using Xunit;

namespace Saturn.Tests.Tools
{
    public class SpawnAgentToolTests : IDisposable
    {
        public SpawnAgentToolTests()
        {
            AgentManager.Instance.SetParentSessionId(null);
            AgentManager.Instance.SetParentModel(null);
        }

        public void Dispose()
        {
            AgentManager.Instance.TerminateAllAgents();
        }

        private static Dictionary<string, object> Params(string name, string task, params (string key, object value)[] extra)
        {
            var parameters = new Dictionary<string, object>
            {
                ["name"] = name,
                ["task"] = task
            };
            foreach (var (key, value) in extra)
            {
                parameters[key] = value;
            }
            return parameters;
        }

        private static async Task WaitForAgentCountAsync(int expected, int timeoutMs = 2000)
        {
            var start = DateTime.Now;
            while ((DateTime.Now - start).TotalMilliseconds < timeoutMs)
            {
                if (AgentManager.Instance.GetCurrentAgentCount() == expected)
                {
                    return;
                }
                await Task.Delay(50);
            }
            AgentManager.Instance.GetCurrentAgentCount().Should().Be(expected);
        }

        [Fact]
        public void Schema_RequiresNameAndTask_BackgroundDefaultsFalse()
        {
            var tool = new SpawnAgentTool();
            var schema = tool.GetParameters();

            var required = (string[])schema["required"];
            required.Should().BeEquivalentTo(new[] { "name", "task" });

            var properties = (Dictionary<string, object>)schema["properties"];
            properties.Keys.Should().Contain(new[] { "name", "task", "purpose", "background", "timeout_seconds" });

            var background = (Dictionary<string, object>)properties["background"];
            background["default"].Should().Be(false);
        }

        [Fact]
        public async Task Execute_SyncSpawn_ReturnsReportAndReleasesAgent()
        {
            var client = new FakeLlmClient { ResponseContent = "report: all files reviewed" };
            AgentManager.Instance.Initialize(new StaticClientSource(client, "fake"));
            var baseline = AgentManager.Instance.GetCurrentAgentCount();

            var tool = new SpawnAgentTool();
            var result = await tool.ExecuteAsync(Params("explorer", "Review the files and report back."));

            result.Success.Should().BeTrue();
            result.FormattedOutput.Should().Contain("report: all files reviewed");
            result.FormattedOutput.Should().Contain("explorer completed");

            await WaitForAgentCountAsync(baseline);
        }

        [Fact]
        public async Task Execute_BackgroundSpawn_ReturnsTaskIdAndCleansUpOnCompletion()
        {
            var client = new FakeLlmClient { ResponseContent = "done" };
            AgentManager.Instance.Initialize(new StaticClientSource(client, "fake"));
            var baseline = AgentManager.Instance.GetCurrentAgentCount();

            var tool = new SpawnAgentTool();
            var result = await tool.ExecuteAsync(Params("worker", "Do the thing.", ("background", true)));

            result.Success.Should().BeTrue();
            var data = (Dictionary<string, object>)result.RawData!;
            data["status"].Should().Be("running");
            var taskId = (string)data["task_id"];

            var completed = await AgentManager.Instance.WaitForAllTasks(new List<string> { taskId }, 5000);
            completed.Should().ContainSingle(r => r.TaskId == taskId && r.Success && r.Result.Contains("done"));

            await WaitForAgentCountAsync(baseline);
        }

        [Fact]
        public async Task Execute_SyncSpawnTimeout_ReturnsTaskIdAndAgentKeepsRunning()
        {
            var client = new BlockingLlmClient();
            AgentManager.Instance.Initialize(new StaticClientSource(client, "fake"));
            var baseline = AgentManager.Instance.GetCurrentAgentCount();

            var tool = new SpawnAgentTool();
            var result = await tool.ExecuteAsync(Params("slow-worker", "Long task.", ("timeout_seconds", 1)));

            result.Success.Should().BeTrue();
            var data = (Dictionary<string, object>)result.RawData!;
            data["status"].Should().Be("running");
            var taskId = (string)data["task_id"];
            AgentManager.Instance.GetTaskResult(taskId).Should().BeNull();

            client.Unblock();

            var completed = await AgentManager.Instance.WaitForAllTasks(new List<string> { taskId }, 5000);
            completed.Should().ContainSingle(r => r.TaskId == taskId && r.Success);

            await WaitForAgentCountAsync(baseline);
        }

        [Fact]
        public async Task Execute_SubAgentError_SurfacesAsToolFailureAndReleasesAgent()
        {
            var client = new FakeLlmClient();
            client.ChatExceptionQueue.Enqueue(new InvalidOperationException("boom"));
            AgentManager.Instance.Initialize(new StaticClientSource(client, "fake"));
            var baseline = AgentManager.Instance.GetCurrentAgentCount();

            var tool = new SpawnAgentTool();
            var result = await tool.ExecuteAsync(Params("failing-worker", "Fail please."));

            result.Success.Should().BeFalse();
            result.Error.Should().Contain("boom");

            await WaitForAgentCountAsync(baseline);
        }

        private sealed class BlockingLlmClient : ILlmClient
        {
            private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public LlmClientCapabilities Capabilities { get; } = new();

            public void Unblock() => _gate.TrySetResult();

            public async Task<ChatCompletionResponse?> ChatAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default)
            {
                await _gate.Task.WaitAsync(cancellationToken);
                return new ChatCompletionResponse
                {
                    Choices = new[]
                    {
                        new Choice
                        {
                            Message = new AssistantMessageResponse { Role = "assistant", Content = "finished late" },
                            FinishReason = "stop"
                        }
                    }
                };
            }

            public async IAsyncEnumerable<ChatCompletionChunk> StreamChatAsync(
                ChatCompletionRequest request,
                [EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                await _gate.Task.WaitAsync(cancellationToken);
                yield break;
            }

            public Task<List<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
                => Task.FromResult(new List<ModelInfo>());

            public Task<bool> ValidateConnectionAsync(CancellationToken cancellationToken = default)
                => Task.FromResult(true);

            public void Dispose()
            {
            }
        }
    }
}
