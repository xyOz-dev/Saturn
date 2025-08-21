using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Saturn.OpenRouter;
using Saturn.OpenRouter.Models.Api.Chat;
using Saturn.Providers.Models;

namespace Saturn.Providers.OpenRouter
{
    public class OpenRouterClientWrapper : ILLMClient
    {
        private readonly OpenRouterClient _client;
        
        public OpenRouterClientWrapper(OpenRouterClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }
        
        public string ProviderName => "OpenRouter";
        public bool IsReady => _client != null;
        
        public async Task<ChatResponse> ChatCompletionAsync(
            ChatRequest request,
            CancellationToken cancellationToken = default)
        {
            // Convert our generic request to OpenRouter format
            var openRouterRequest = ConvertToOpenRouterRequest(request);
            
            // Call OpenRouter API
            var response = await _client.Chat.CreateAsync(
                openRouterRequest, 
                cancellationToken);
            
            // Convert response back to our format
            return ConvertFromOpenRouterResponse(response);
        }
        
        public async Task<ChatResponse> StreamChatAsync(
            ChatRequest request,
            Func<Saturn.Providers.Models.StreamChunk, Task> onChunk,
            CancellationToken cancellationToken = default)
        {
            var openRouterRequest = ConvertToOpenRouterRequest(request);
            openRouterRequest.Stream = true;
            
            var finalResponse = new ChatResponse
            {
                Message = new ChatMessage { Role = "assistant", Content = "" }
            };
            
            await foreach (var chunk in _client.ChatStreaming.StreamAsync(openRouterRequest, cancellationToken))
            {
                var streamChunk = new Saturn.Providers.Models.StreamChunk
                {
                    Id = chunk.Id ?? "",
                    Delta = chunk.Choices?.FirstOrDefault()?.Delta?.Content ?? "",
                    IsComplete = chunk.Choices?.FirstOrDefault()?.FinishReason != null
                };
                
                finalResponse.Message.Content += streamChunk.Delta;
                finalResponse.Id = chunk.Id ?? "";
                finalResponse.Model = chunk.Model ?? "";
                
                // Handle tool calls in streaming
                var toolCalls = chunk.Choices?.FirstOrDefault()?.Delta?.ToolCalls;
                if (toolCalls != null && toolCalls.Length > 0)
                {
                    streamChunk.ToolCall = new Saturn.Providers.Models.ToolCall
                    {
                        Id = toolCalls[0].Id ?? "",
                        Name = toolCalls[0].Function?.Name ?? "",
                        Arguments = toolCalls[0].Function?.Arguments ?? ""
                    };
                }
                
                await onChunk(streamChunk);
            }
            
            return finalResponse;
        }
        
        public async Task<List<ModelInfo>> GetModelsAsync()
        {
            var models = await _client.Models.ListAllAsync();
            
            return models?.Data?.Select(m => new ModelInfo
            {
                Id = m.Id ?? "",
                Name = m.Name ?? m.Id ?? "",
                Provider = m.Id?.Split('/')[0] ?? "",
                MaxTokens = m.ContextLength ?? 0,
                InputCost = double.TryParse(m.Pricing?.Prompt, out var promptCost) ? promptCost : 0,
                OutputCost = double.TryParse(m.Pricing?.Completion, out var completionCost) ? completionCost : 0
            }).ToList() ?? new List<ModelInfo>();
        }
        
        private ChatCompletionRequest ConvertToOpenRouterRequest(ChatRequest request)
        {
            return new ChatCompletionRequest
            {
                Model = request.Model,
                Messages = request.Messages.Select(m => new Message
                {
                    Role = m.Role,
                    Content = JsonDocument.Parse($"\"{m.Content}\"").RootElement,
                    ToolCallId = m.ToolCallId,
                    ToolCalls = m.ToolCalls?.Select(tc => new ToolCallRequest
                    {
                        Id = tc.Id,
                        Type = "function",
                        Function = new ToolCallRequest.FunctionCall
                        {
                            Name = tc.Name,
                            Arguments = tc.Arguments
                        }
                    }).ToArray()
                }).ToArray(),
                Temperature = request.Temperature,
                MaxTokens = request.MaxTokens,
                TopP = request.TopP,
                Tools = request.Tools?.Select(t => new Saturn.OpenRouter.Models.Api.Chat.ToolDefinition
                {
                    Type = "function",
                    Function = new ToolFunction
                    {
                        Name = t.Name,
                        Description = t.Description,
                        Parameters = JsonSerializer.SerializeToElement(t.Parameters ?? new { })
                    }
                }).ToArray(),
                Stream = request.Stream
            };
        }
        
        private ChatResponse ConvertFromOpenRouterResponse(ChatCompletionResponse? response)
        {
            if (response == null)
            {
                return new ChatResponse
                {
                    Message = new ChatMessage { Role = "assistant", Content = "" }
                };
            }
            
            var choice = response.Choices?.FirstOrDefault();
            var message = choice?.Message;
            
            return new ChatResponse
            {
                Id = response.Id ?? "",
                Model = response.Model ?? "",
                Message = new ChatMessage
                {
                    Role = message?.Role ?? "assistant",
                    Content = message?.Content ?? "",
                    ToolCalls = message?.ToolCalls?.Select(tc => new Saturn.Providers.Models.ToolCall
                    {
                        Id = tc.Id ?? "",
                        Name = tc.Function?.Name ?? "",
                        Arguments = tc.Function?.Arguments ?? ""
                    }).ToList()
                },
                Usage = new Usage
                {
                    InputTokens = response.Usage?.PromptTokens ?? 0,
                    OutputTokens = response.Usage?.CompletionTokens ?? 0,
                    TotalTokens = response.Usage?.TotalTokens ?? 0
                },
                FinishReason = choice?.FinishReason
            };
        }
    }
}