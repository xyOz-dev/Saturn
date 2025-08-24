using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Saturn.Providers;
using Saturn.Providers.Models;

namespace Saturn.Providers.Anthropic
{
    public class AnthropicClientWrapper : ILLMClient
    {
        private readonly AnthropicClient _client;
        
        public AnthropicClientWrapper(AnthropicClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }
        
        public string ProviderName => "Anthropic";
        public bool IsReady => _client?.IsReady ?? false;
        
        public async Task<ChatResponse> ChatCompletionAsync(
            ChatRequest request,
            CancellationToken cancellationToken = default)
        {
            return await _client.ChatCompletionAsync(request, cancellationToken);
        }
        
        public async Task<ChatResponse> StreamChatAsync(
            ChatRequest request,
            Func<StreamChunk, Task> onChunk,
            CancellationToken cancellationToken = default)
        {
            return await _client.StreamChatAsync(request, onChunk, cancellationToken);
        }
        
        public async Task<List<ModelInfo>> GetModelsAsync()
        {
            return await _client.GetModelsAsync();
        }
        
        public void Dispose()
        {
            _client?.Dispose();
        }
    }
}