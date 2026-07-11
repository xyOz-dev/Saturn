using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Saturn.OpenRouter.Models.Api.Chat;

namespace Saturn.Providers
{
    /// <summary>
    /// A connected chat-completions backend. All providers speak the OpenAI-compatible
    /// dialect, so the request/response DTOs from the OpenRouter SDK serve as the shared
    /// wire format; providers that don't understand an extension field simply never
    /// receive it (see <see cref="LlmClientCapabilities"/>).
    /// </summary>
    public interface ILlmClient : IDisposable
    {
        LlmClientCapabilities Capabilities { get; }

        Task<ChatCompletionResponse?> ChatAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default);

        IAsyncEnumerable<ChatCompletionChunk> StreamChatAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default);

        Task<List<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default);

        /// <summary>Cheap liveness/auth probe. Returns false rather than throwing on failure.</summary>
        Task<bool> ValidateConnectionAsync(CancellationToken cancellationToken = default);
    }
}
