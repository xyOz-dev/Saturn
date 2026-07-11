using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Saturn.Agents.Core;
using Saturn.OpenRouter.Models.Api.Chat;
using Saturn.Providers;

namespace Saturn.Tests.Providers
{
    public class FakeLlmClient : ILlmClient
    {
        public LlmClientCapabilities Capabilities { get; set; } = new();
        public List<ModelInfo> Models { get; set; } = new();
        public bool ValidateResult { get; set; } = true;
        public bool Disposed { get; private set; }
        public ChatCompletionRequest? LastRequest { get; private set; }
        public List<ChatCompletionRequest> Requests { get; } = new();
        public string ResponseContent { get; set; } = "ok";

        /// <summary>Scripted responses returned in order; when empty, a plain text reply is returned.</summary>
        public Queue<ChatCompletionResponse> ResponseQueue { get; } = new();

        public Task<ChatCompletionResponse?> ChatAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            Requests.Add(request);

            if (ResponseQueue.Count > 0)
            {
                return Task.FromResult<ChatCompletionResponse?>(ResponseQueue.Dequeue());
            }

            return Task.FromResult<ChatCompletionResponse?>(new ChatCompletionResponse
            {
                Choices = new[]
                {
                    new Choice
                    {
                        Message = new AssistantMessageResponse { Role = "assistant", Content = ResponseContent },
                        FinishReason = "stop"
                    }
                }
            });
        }

        public async IAsyncEnumerable<ChatCompletionChunk> StreamChatAsync(
            ChatCompletionRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            await Task.CompletedTask;
            yield break;
        }

        public Task<List<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new List<ModelInfo>(Models));

        public Task<bool> ValidateConnectionAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(ValidateResult);

        public void Dispose() => Disposed = true;
    }

    public class FakeProvider : ILlmProvider
    {
        public string Name { get; init; } = "fake";
        public string DisplayName { get; init; } = "Fake";
        public IReadOnlyList<ProviderSettingDescriptor> SettingDescriptors { get; init; } =
            Array.Empty<ProviderSettingDescriptor>();
        public Func<ProviderSettings, ILlmClient> Factory { get; init; } = _ => new FakeLlmClient();

        public Task<ILlmClient> CreateClientAsync(ProviderSettings settings, CancellationToken cancellationToken = default)
            => Task.FromResult(Factory(settings));
    }

    /// <summary>Agent that skips SQLite persistence so tests stay on the fake client only.</summary>
    public class TestAgent : AgentBase
    {
        public TestAgent(AgentConfiguration configuration) : base(configuration)
        {
        }

        protected override void InitializeRepository()
        {
        }
    }

    public class StubHttpHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        private readonly List<(HttpRequestMessage Request, string Body)> _log;

        public StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responder, List<(HttpRequestMessage, string)> log)
        {
            _responder = responder;
            _log = log;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            lock (_log)
            {
                _log.Add((request, body));
            }
            return _responder(request);
        }

        public static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK)
        {
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        }
    }
}
