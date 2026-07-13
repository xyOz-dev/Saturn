using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Saturn.Data;
using Xunit;

namespace Saturn.Tests.Data
{
    public class SessionTodosRepositoryTests : IDisposable
    {
        private readonly string _workspace;

        public SessionTodosRepositoryTests()
        {
            _workspace = Path.Combine(Path.GetTempPath(), $"SaturnTodosTest_{Guid.NewGuid():N}");
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

        [Fact]
        public async Task SaveAndGet_RoundTripsAcrossRepositoryInstances()
        {
            var sessionId = Guid.NewGuid().ToString();
            var json = "[{\"content\":\"Step one\",\"status\":\"in_progress\"}]";

            using (var repository = new ChatHistoryRepository(_workspace))
            {
                await repository.SaveSessionTodosAsync(sessionId, json);
            }

            using var fresh = new ChatHistoryRepository(_workspace);
            var loaded = await fresh.GetSessionTodosAsync(sessionId);

            loaded.Should().Be(json);
        }

        [Fact]
        public async Task Save_Twice_OverwritesExistingRow()
        {
            var sessionId = Guid.NewGuid().ToString();
            using var repository = new ChatHistoryRepository(_workspace);

            await repository.SaveSessionTodosAsync(sessionId, "[\"old\"]");
            await repository.SaveSessionTodosAsync(sessionId, "[\"new\"]");

            (await repository.GetSessionTodosAsync(sessionId)).Should().Be("[\"new\"]");
        }

        [Fact]
        public async Task Save_WithNullJson_DeletesRow()
        {
            var sessionId = Guid.NewGuid().ToString();
            using var repository = new ChatHistoryRepository(_workspace);

            await repository.SaveSessionTodosAsync(sessionId, "[\"data\"]");
            await repository.SaveSessionTodosAsync(sessionId, null);

            (await repository.GetSessionTodosAsync(sessionId)).Should().BeNull();
        }

        [Fact]
        public async Task Get_ForUnknownSession_ReturnsNull()
        {
            using var repository = new ChatHistoryRepository(_workspace);

            (await repository.GetSessionTodosAsync(Guid.NewGuid().ToString())).Should().BeNull();
        }
    }
}
