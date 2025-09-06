using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System;
using Saturn.Providers.Models;

namespace Saturn.Providers
{
    public interface ILLMClient
    {
        // Basic chat completion
        Task<ChatResponse> ChatCompletionAsync(
            ChatRequest request,
            CancellationToken cancellationToken = default);
        
        // Streaming chat completion
        Task<ChatResponse> StreamChatAsync(
            ChatRequest request,
            Func<StreamChunk, Task> onChunk,
            CancellationToken cancellationToken = default);
        
        // Get available models
        Task<List<ModelInfo>> GetModelsAsync();
        
        // Get provider name
        string ProviderName { get; }
        
        // Check if client is ready
        bool IsReady { get; }
    }
}