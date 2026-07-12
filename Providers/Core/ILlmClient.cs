using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Saturn.OpenRouter.Models.Api.Chat;

namespace Saturn.Providers
{
    public interface ILlmClient : IDisposable
    {
        LlmClientCapabilities Capabilities { get; }

        Task<ChatCompletionResponse?> ChatAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default);

        IAsyncEnumerable<ChatCompletionChunk> StreamChatAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default);

        Task<List<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default);

        Task<bool> ValidateConnectionAsync(CancellationToken cancellationToken = default);
    }
}
