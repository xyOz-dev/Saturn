using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Saturn.Agents;
using Saturn.Agents.MultiAgent;
using Saturn.Data;

namespace Saturn.Web
{
    public class TranscriptEntry
    {
        public string Role { get; init; } = "";
        public string Content { get; init; } = "";
        public string Source { get; init; } = "user";
        public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    }

    public class OrchestratorService
    {
        private readonly Agent _agent;
        private readonly EventHub _hub;
        private readonly List<TranscriptEntry> _transcript = new();
        private readonly object _transcriptLock = new();
        private CancellationTokenSource? _cts;
        private int _busy;
        private bool _parentWired;

        public event Action? OnIdle;

        public OrchestratorService(Agent agent, EventHub hub)
        {
            _agent = agent;
            _hub = hub;
            _agent.OnToolCall += (toolName, arguments) =>
                _hub.Publish("orchestrator.toolcall", new { toolName, arguments });
        }

        public bool IsBusy => Volatile.Read(ref _busy) == 1;

        public string Model => _agent.Configuration.Model;

        public List<TranscriptEntry> GetTranscript()
        {
            lock (_transcriptLock)
            {
                return _transcript.ToList();
            }
        }

        public async Task RestoreTranscriptAsync(ChatHistoryRepository history)
        {
            try
            {
                var sessions = await history.GetSessionsAsync("main", 1);
                if (sessions.Count == 0)
                {
                    return;
                }

                var messages = await history.GetMessagesAsync(sessions[0].Id);
                // Tool-call turns are stored with a literal "null" content; they are not part of the visible conversation.
                var restored = messages
                    .Where(m => (m.Role == "user" || m.Role == "assistant")
                        && !string.IsNullOrWhiteSpace(m.Content)
                        && m.Content != "null")
                    .Select(m => new TranscriptEntry { Role = m.Role, Content = m.Content, Timestamp = m.Timestamp })
                    .ToList();

                if (restored.Count == 0)
                {
                    return;
                }

                lock (_transcriptLock)
                {
                    if (_transcript.Count == 0)
                    {
                        _transcript.AddRange(restored);
                    }
                }

                // Continue the previous session and give the model its memory back,
                // so long-horizon work survives process restarts.
                _agent.CurrentSessionId = sessions[0].Id;
                _agent.RehydrateHistory(restored.Select(e => (e.Role, e.Content)).ToList());
            }
            catch (Exception)
            {
                // Restoring history is best-effort; an unreadable database should not block startup.
            }
        }

        public bool TrySend(string message, string source = "user")
        {
            if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
            {
                return false;
            }

            AddEntry("user", message, source);
            _hub.Publish("orchestrator.state", new { busy = true });

            var cts = new CancellationTokenSource();
            _cts = cts;

            _ = Task.Run(async () =>
            {
                var buffer = new StringBuilder();
                try
                {
                    await EnsureSessionAsync();

                    await _agent.ExecuteStreamAsync(message, chunk =>
                    {
                        if (!string.IsNullOrEmpty(chunk.Content) && !chunk.IsToolCall)
                        {
                            buffer.Append(chunk.Content);
                            _hub.Publish("orchestrator.chunk", new { content = chunk.Content });
                        }
                        return Task.CompletedTask;
                    }, cts.Token);

                    AddEntry("assistant", buffer.ToString());
                }
                catch (OperationCanceledException)
                {
                    AddEntry("system", "Response cancelled.");
                }
                catch (Exception ex)
                {
                    AddEntry("system", $"Error: {ex.Message}");
                }
                finally
                {
                    cts.Dispose();
                    if (ReferenceEquals(_cts, cts))
                    {
                        _cts = null;
                    }
                    Volatile.Write(ref _busy, 0);
                    _hub.Publish("orchestrator.state", new { busy = false });
                    try
                    {
                        OnIdle?.Invoke();
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"OnIdle handler error: {ex.Message}");
                    }
                }
            });

            return true;
        }

        public void Cancel()
        {
            try
            {
                _cts?.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private async Task EnsureSessionAsync()
        {
            if (_agent.CurrentSessionId == null)
            {
                await _agent.InitializeSessionAsync("main");
            }

            if (!_parentWired)
            {
                AgentManager.Instance.SetParentSessionId(_agent.CurrentSessionId);
                AgentManager.Instance.SetParentModel(_agent.Configuration.Model);
                AgentManager.Instance.SetParentEnableUserRules(_agent.Configuration.EnableUserRules);
                _parentWired = true;
            }
        }

        private void AddEntry(string role, string content, string source = "user")
        {
            var entry = new TranscriptEntry { Role = role, Content = content, Source = source };
            lock (_transcriptLock)
            {
                _transcript.Add(entry);
            }
            _hub.Publish("orchestrator.message", entry);
        }
    }
}
