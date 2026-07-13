using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Saturn.Agents.Core;
using Saturn.Tests.Approvals;
using Saturn.Tools.Core;
using Saturn.Tools.Todo;
using Xunit;

namespace Saturn.Tests.Tools
{
    public class UpdateTodosToolTests : IDisposable
    {
        public void Dispose()
        {
            AgentContext.Current = null;
        }

        private static void SetSession(string? sessionId, string agentInstanceId = "")
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
                AgentInstanceId = agentInstanceId,
                SessionId = sessionId
            };
        }

        private static Dictionary<string, object> Item(string content, string status)
        {
            return new Dictionary<string, object>
            {
                { "content", content },
                { "status", status }
            };
        }

        private static Dictionary<string, object> Params(params object[] items)
        {
            return new Dictionary<string, object>
            {
                { "todos", new List<object>(items) }
            };
        }

        [Fact]
        public async Task Execute_WithValidList_ReturnsChecklist()
        {
            SetSession($"session-{Guid.NewGuid():N}");
            var tool = new UpdateTodosTool();

            var result = await tool.ExecuteAsync(Params(
                Item("Explore the codebase", "completed"),
                Item("Implement the tool", "in_progress"),
                Item("Add tests", "pending")));

            result.Success.Should().BeTrue();
            result.FormattedOutput.Should().Contain("3 items: 1 completed, 1 in progress, 1 pending");
            result.FormattedOutput.Should().Contain("[x] Explore the codebase");
            result.FormattedOutput.Should().Contain("[~] Implement the tool");
            result.FormattedOutput.Should().Contain("[ ] Add tests");
        }

        [Fact]
        public async Task Execute_WithEmptyArray_ClearsList()
        {
            var key = $"session-{Guid.NewGuid():N}";
            SetSession(key);
            var tool = new UpdateTodosTool();

            await tool.ExecuteAsync(Params(Item("Step one", "pending")));
            var result = await tool.ExecuteAsync(Params());

            result.Success.Should().BeTrue();
            result.FormattedOutput.Should().Be("Todo list cleared.");
            (await TodoStore.GetAsync(key)).Should().BeEmpty();
        }

        [Fact]
        public async Task Execute_WithEmptyContent_ReturnsErrorNamingIndex()
        {
            SetSession($"session-{Guid.NewGuid():N}");
            var tool = new UpdateTodosTool();

            var result = await tool.ExecuteAsync(Params(
                Item("Valid step", "pending"),
                Item("", "pending")));

            result.Success.Should().BeFalse();
            result.Error.Should().Contain("todos[1].content");
        }

        [Fact]
        public async Task Execute_WithInvalidStatus_ReturnsErrorListingValidValues()
        {
            SetSession($"session-{Guid.NewGuid():N}");
            var tool = new UpdateTodosTool();

            var result = await tool.ExecuteAsync(Params(Item("Step", "done")));

            result.Success.Should().BeFalse();
            result.Error.Should().Contain("pending, in_progress, completed");
        }

        [Fact]
        public async Task Execute_WithTwoInProgressItems_ReturnsError()
        {
            SetSession($"session-{Guid.NewGuid():N}");
            var tool = new UpdateTodosTool();

            var result = await tool.ExecuteAsync(Params(
                Item("First", "in_progress"),
                Item("Second", "in_progress")));

            result.Success.Should().BeFalse();
            result.Error.Should().Contain("Only one item may be in_progress");
        }

        [Fact]
        public async Task Execute_WithNonObjectItem_ReturnsError()
        {
            SetSession($"session-{Guid.NewGuid():N}");
            var tool = new UpdateTodosTool();

            var result = await tool.ExecuteAsync(Params("just a string"));

            result.Success.Should().BeFalse();
            result.Error.Should().Contain("todos[0]");
        }

        [Fact]
        public async Task Execute_WithTooManyItems_ReturnsError()
        {
            SetSession($"session-{Guid.NewGuid():N}");
            var tool = new UpdateTodosTool();
            var items = Enumerable.Range(0, 51).Select(i => (object)Item($"Step {i}", "pending")).ToArray();

            var result = await tool.ExecuteAsync(Params(items));

            result.Success.Should().BeFalse();
            result.Error.Should().Contain("at most 50");
        }

        [Fact]
        public async Task Execute_SecondCall_ReplacesEntireList()
        {
            var key = $"session-{Guid.NewGuid():N}";
            SetSession(key);
            var tool = new UpdateTodosTool();

            await tool.ExecuteAsync(Params(Item("Old step", "pending")));
            await tool.ExecuteAsync(Params(Item("New step", "in_progress")));

            var todos = await TodoStore.GetAsync(key);
            todos.Should().HaveCount(1);
            todos[0].Content.Should().Be("New step");
            todos[0].Status.Should().Be(TodoStatus.InProgress);
        }

        [Fact]
        public async Task Execute_DifferentSessions_KeepIndependentLists()
        {
            var keyA = $"session-{Guid.NewGuid():N}";
            var keyB = $"session-{Guid.NewGuid():N}";
            var tool = new UpdateTodosTool();

            SetSession(keyA);
            await tool.ExecuteAsync(Params(Item("Session A step", "pending")));

            SetSession(keyB);
            await tool.ExecuteAsync(Params(Item("Session B step", "pending")));

            (await TodoStore.GetAsync(keyA)).Single().Content.Should().Be("Session A step");
            (await TodoStore.GetAsync(keyB)).Single().Content.Should().Be("Session B step");
        }

        [Fact]
        public async Task Execute_WithoutSession_UsesFallbackKey()
        {
            AgentContext.Current = null;
            var tool = new UpdateTodosTool();

            var result = await tool.ExecuteAsync(Params(Item("Contextless step", "pending")));

            result.Success.Should().BeTrue();
            result.FormattedOutput.Should().NotContain("Warning");
            TodoStore.CurrentKey().Should().Be(TodoStore.NoSessionKey);
            (await TodoStore.GetAsync(TodoStore.NoSessionKey)).Should().Contain(t => t.Content == "Contextless step");

            await TodoStore.SetAsync(TodoStore.NoSessionKey, Array.Empty<TodoItem>());
        }

        [Fact]
        public async Task Execute_SessionlessAgents_DoNotShareLists()
        {
            var agentA = $"agent-a-{Guid.NewGuid():N}";
            var agentB = $"agent-b-{Guid.NewGuid():N}";
            var tool = new UpdateTodosTool();

            SetSession(null, agentA);
            var keyA = TodoStore.CurrentKey();
            await tool.ExecuteAsync(Params(Item("Agent A step", "pending")));

            SetSession(null, agentB);
            var keyB = TodoStore.CurrentKey();
            await tool.ExecuteAsync(Params(Item("Agent B step", "pending")));

            keyA.Should().NotBe(keyB);
            keyA.Should().NotBe(TodoStore.NoSessionKey);
            (await TodoStore.GetAsync(keyA)).Single().Content.Should().Be("Agent A step");
            (await TodoStore.GetAsync(keyB)).Single().Content.Should().Be("Agent B step");
        }

        [Fact]
        public async Task Cache_EvictsLeastRecentlyUsedEntriesOverCap()
        {
            var prefix = $"evict-{Guid.NewGuid():N}";
            var oldestKey = $"(agent:{prefix}-0)";
            var items = new[] { new TodoItem("Step", TodoStatus.Pending) };

            await TodoStore.SetAsync(oldestKey, items);
            for (var i = 1; i <= 300; i++)
            {
                await TodoStore.SetAsync($"(agent:{prefix}-{i})", items);
            }

            (await TodoStore.GetAsync(oldestKey)).Should().BeEmpty();
            (await TodoStore.GetAsync($"(agent:{prefix}-300)")).Should().HaveCount(1);
        }

        [Fact]
        public async Task Execute_WithMissingTodosType_ReturnsError()
        {
            SetSession($"session-{Guid.NewGuid():N}");
            var tool = new UpdateTodosTool();

            var result = await tool.ExecuteAsync(new Dictionary<string, object> { { "todos", "not a list" } });

            result.Success.Should().BeFalse();
            result.Error.Should().Contain("array");
        }

        [Fact]
        public void GetDisplaySummary_ReportsItemCount()
        {
            var tool = new UpdateTodosTool();

            tool.GetDisplaySummary(Params(Item("A", "pending"), Item("B", "pending")))
                .Should().Be("Updating todo list (2 items)");
            tool.GetDisplaySummary(Params()).Should().Be("Clearing todo list");
        }
    }
}
