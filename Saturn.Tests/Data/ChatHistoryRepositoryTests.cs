using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Saturn.Data;
using Saturn.OpenRouter.Models.Api.Chat;
using Xunit;

namespace Saturn.Tests.Data
{
    public class ChatHistoryRepositoryTests : IDisposable
    {
        private readonly string _workspace;

        public ChatHistoryRepositoryTests()
        {
            _workspace = Path.Combine(Path.GetTempPath(), $"SaturnHistoryTest_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_workspace);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_workspace, recursive: true);
            }
            catch
            {
            }
        }

        private static JsonElement JsonNull() => JsonDocument.Parse("null").RootElement;

        [Fact]
        public async Task SaveMessage_WithNullContent_StoresEmptyStringNotLiteralNull()
        {
            using var repository = new ChatHistoryRepository(_workspace);
            var session = await repository.CreateSessionAsync("test");

            var toolCallMessage = new Message
            {
                Role = "assistant",
                Content = JsonNull(),
                ToolCalls = new[]
                {
                    new ToolCallRequest
                    {
                        Id = "call_1",
                        Function = new ToolCallRequest.FunctionCall { Name = "grep_files", Arguments = "{}" }
                    }
                }
            };

            await repository.SaveMessageAsync(session.Id, toolCallMessage, "TestAgent");

            var messages = await repository.GetMessagesAsync(session.Id);
            messages.Should().ContainSingle();
            messages[0].Content.Should().BeEmpty();
            messages[0].ToolCallsJson.Should().Contain("grep_files");
        }

        [Fact]
        public async Task SaveMessageBatch_WithNullContent_StoresEmptyStringNotLiteralNull()
        {
            using var repository = new ChatHistoryRepository(_workspace);
            var session = await repository.CreateSessionAsync("test");

            var messages = new[]
            {
                new Message { Role = "assistant", Content = JsonNull() },
                new Message { Role = "assistant", Content = JsonSerializer.SerializeToElement("real text") }
            }.ToList();

            await repository.SaveMessageBatchAsync(session.Id, messages, "TestAgent");

            var loaded = await repository.GetMessagesAsync(session.Id);
            loaded.Should().HaveCount(2);
            loaded[0].Content.Should().BeEmpty();
            loaded[1].Content.Should().Be("real text");
        }

        [Fact]
        public async Task SaveMessage_WithStringContent_RoundTrips()
        {
            using var repository = new ChatHistoryRepository(_workspace);
            var session = await repository.CreateSessionAsync("test");

            var message = new Message
            {
                Role = "user",
                Content = JsonSerializer.SerializeToElement("hello world")
            };

            await repository.SaveMessageAsync(session.Id, message);

            var messages = await repository.GetMessagesAsync(session.Id);
            messages.Should().ContainSingle();
            messages[0].Content.Should().Be("hello world");
        }
    }
}
