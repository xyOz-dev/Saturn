using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Saturn.Agents;
using Saturn.Agents.MultiAgent;

namespace Saturn.Web
{
    public class TranscriptEntry
    {
        public string Role { get; init; } = "";
        public string Content { get; init; } = "";
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

        public bool TrySend(string message)
        {
            if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
            {
                return false;
            }

            AddEntry("user", message);
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
                AgentManager.Instance.SetParentSessionId(_agent.CurrentSessionId);
                AgentManager.Instance.SetParentModel(_agent.Configuration.Model);
                AgentManager.Instance.SetParentEnableUserRules(_agent.Configuration.EnableUserRules);
            }
        }

        private void AddEntry(string role, string content)
        {
            var entry = new TranscriptEntry { Role = role, Content = content };
            lock (_transcriptLock)
            {
                _transcript.Add(entry);
            }
            _hub.Publish("orchestrator.message", entry);
        }
    }
}
