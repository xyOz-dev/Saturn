using Saturn.Providers;
using Saturn.Providers.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Saturn.Tests.Mocks
{
    public class MockLLMClient : ILLMClient
    {
        public string ProviderName { get; set; } = "Mock";
        public bool IsReady { get; set; } = true;
        
        public List<ChatRequest> ReceivedRequests { get; } = new();
        public ChatResponse? ResponseToReturn { get; set; }
        public Exception? ExceptionToThrow { get; set; }
        public List<ModelInfo> ModelsToReturn { get; set; } = new();
        
        public Task<ChatResponse> ChatCompletionAsync(
            ChatRequest request,
            CancellationToken cancellationToken = default)
        {
            ReceivedRequests.Add(request);
            
            if (ExceptionToThrow != null)
            {
                throw ExceptionToThrow;
            }
            
            return Task.FromResult(ResponseToReturn ?? new ChatResponse
            {
                Id = "mock_response",
                Model = request.Model,
                Message = new ChatMessage
                {
                    Role = "assistant",
                    Content = "Mock response"
                },
                Usage = new Usage
                {
                    InputTokens = 10,
                    OutputTokens = 20,
                    TotalTokens = 30
                }
            });
        }
        
        public async Task<ChatResponse> StreamChatAsync(
            ChatRequest request,
            Func<StreamChunk, Task> onChunk,
            CancellationToken cancellationToken = default)
        {
            ReceivedRequests.Add(request);
            
            if (ExceptionToThrow != null)
            {
                throw ExceptionToThrow;
            }
            
            var response = "Mock streaming response";
            for (int i = 0; i < response.Length; i++)
            {
                await onChunk(new StreamChunk
                {
                    Id = "chunk_" + i,
                    Delta = response[i].ToString(),
                    IsComplete = false
                });
            }
            
            await onChunk(new StreamChunk
            {
                Id = "chunk_final",
                IsComplete = true
            });
            
            return new ChatResponse
            {
                Id = "mock_stream_response",
                Model = request.Model,
                Message = new ChatMessage
                {
                    Role = "assistant",
                    Content = response
                },
                Usage = new Usage
                {
                    InputTokens = 10,
                    OutputTokens = response.Length,
                    TotalTokens = 10 + response.Length
                }
            };
        }
        
        public Task<List<ModelInfo>> GetModelsAsync()
        {
            if (ExceptionToThrow != null)
            {
                throw ExceptionToThrow;
            }
            
            if (ModelsToReturn.Count == 0)
            {
                ModelsToReturn.AddRange(new[]
                {
                    new ModelInfo { Id = "mock-model-1", Name = "Mock Model 1", Provider = "Mock", MaxTokens = 4096 },
                    new ModelInfo { Id = "mock-model-2", Name = "Mock Model 2", Provider = "Mock", MaxTokens = 8192 }
                });
            }
            
            return Task.FromResult(ModelsToReturn);
        }
        
        public void Dispose()
        {
            // Mock disposal
        }
    }
}