using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Saturn.Providers;
using Saturn.Providers.Models;
using Saturn.Providers.Anthropic.Models;
using Saturn.Providers.Anthropic.Utils;

namespace Saturn.Providers.Anthropic
{
    public class AnthropicClient : ILLMClient
    {
        private const string API_BASE = "https://api.anthropic.com";
        private const string MESSAGES_ENDPOINT = "/v1/messages";
        private const string ANTHROPIC_VERSION = "2023-06-01";
        private const string ANTHROPIC_BETA = "oauth-2025-04-20";
        
        private readonly HttpClient _httpClient;
        private readonly AnthropicAuthService _authService;
        
        public AnthropicClient(AnthropicAuthService authService)
        {
            _authService = authService ?? throw new ArgumentNullException(nameof(authService));
            _httpClient = new HttpClient
            {
                BaseAddress = new Uri(API_BASE),
                Timeout = TimeSpan.FromMinutes(5)
            };
        }
        
        public string ProviderName => "Anthropic";
        public bool IsReady => _authService != null;
        
        public async Task<ChatResponse> ChatCompletionAsync(
            ChatRequest request,
            CancellationToken cancellationToken = default)
        {
            // Validate input parameters
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            
            if (request.Messages == null || request.Messages.Count == 0)
                throw new ArgumentException("Request must contain at least one message", nameof(request));
            
            if (string.IsNullOrEmpty(request.Model))
                throw new ArgumentException("Model name cannot be null or empty", nameof(request));
            
            if (string.IsNullOrWhiteSpace(request.Model))
                throw new ArgumentException("Model name cannot be whitespace only", nameof(request));
            
            // Validate message content
            foreach (var message in request.Messages)
            {
                if (message == null)
                    throw new ArgumentException("Message cannot be null", nameof(request));
                
                if (string.IsNullOrEmpty(message.Role))
                    throw new ArgumentException("Message role cannot be null or empty", nameof(request));
                
                if (!IsValidRole(message.Role))
                    throw new ArgumentException($"Invalid message role: {message.Role}. Valid roles are: system, user, assistant", nameof(request));
                
                if (string.IsNullOrEmpty(message.Content) && (message.ToolCalls == null || message.ToolCalls.Count == 0))
                    throw new ArgumentException("Message must have either content or tool calls", nameof(request));
            }
            
            // Validate optional parameters
            if (request.Temperature.HasValue && (request.Temperature.Value < 0 || request.Temperature.Value > 2))
                throw new ArgumentException("Temperature must be between 0 and 2", nameof(request));
            
            if (request.MaxTokens.HasValue && request.MaxTokens.Value <= 0)
                throw new ArgumentException("MaxTokens must be greater than 0", nameof(request));
            
            if (request.TopP.HasValue && (request.TopP.Value < 0 || request.TopP.Value > 1))
                throw new ArgumentException("TopP must be between 0 and 1 (inclusive)", nameof(request));
            
            return await ErrorHandler.ExecuteWithRetryAsync(async () =>
            {
                // Convert to Anthropic format
                var anthropicRequest = MessageConverter.ConvertToAnthropicRequest(request);
                anthropicRequest.Stream = false;
                
                // Prepare request
                var httpRequest = await PrepareRequestAsync(anthropicRequest);
                
                // Send request
                var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
                
                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    var errorMessage = ErrorHandler.ParseErrorMessage(error);
                    throw new HttpRequestException($"Anthropic API error ({response.StatusCode}): {errorMessage}");
                }
                
                // Parse response
                var responseJson = await response.Content.ReadAsStringAsync();
                var anthropicResponse = JsonSerializer.Deserialize<AnthropicChatResponse>(responseJson);
                
                if (anthropicResponse == null)
                {
                    throw new InvalidOperationException("Failed to deserialize Anthropic API response");
                }
                
                // Convert to common format
                var result = MessageConverter.ConvertFromAnthropicResponse(anthropicResponse);
                // Preserve the requested model name instead of what the API returns
                result.Model = anthropicRequest.Model;
                return result;
            }, maxRetries: 3, cancellationToken);
        }
        
        public async Task<ChatResponse> StreamChatAsync(
            ChatRequest request,
            Func<StreamChunk, Task> onChunk,
            CancellationToken cancellationToken = default)
        {
            // StreamChatAsync called
            
            // Validate input parameters
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            
            if (onChunk == null)
                throw new ArgumentNullException(nameof(onChunk));
            
            if (request.Messages == null || request.Messages.Count == 0)
                throw new ArgumentException("Request must contain at least one message", nameof(request));
            
            if (string.IsNullOrEmpty(request.Model))
                throw new ArgumentException("Model name cannot be null or empty", nameof(request));
            
            if (string.IsNullOrWhiteSpace(request.Model))
                throw new ArgumentException("Model name cannot be whitespace only", nameof(request));
            
            // Model and messages validated
            
            // Validate message content (reuse validation logic)
            foreach (var message in request.Messages)
            {
                if (message == null)
                    throw new ArgumentException("Message cannot be null", nameof(request));
                
                if (string.IsNullOrEmpty(message.Role))
                    throw new ArgumentException("Message role cannot be null or empty", nameof(request));
                
                if (!IsValidRole(message.Role))
                    throw new ArgumentException($"Invalid message role: {message.Role}. Valid roles are: system, user, assistant", nameof(request));
                
                if (string.IsNullOrEmpty(message.Content) && (message.ToolCalls == null || message.ToolCalls.Count == 0))
                    throw new ArgumentException("Message must have either content or tool calls", nameof(request));
            }
            
            // Validate optional parameters
            if (request.Temperature.HasValue && (request.Temperature.Value < 0 || request.Temperature.Value > 2))
                throw new ArgumentException("Temperature must be between 0 and 2", nameof(request));
            
            if (request.MaxTokens.HasValue && request.MaxTokens.Value <= 0)
                throw new ArgumentException("MaxTokens must be greater than 0", nameof(request));
            
            if (request.TopP.HasValue && (request.TopP.Value < 0 || request.TopP.Value > 1))
                throw new ArgumentException("TopP must be between 0 and 1 (inclusive)", nameof(request));
            
            // Converting to Anthropic format
            
            // Convert to Anthropic format
            var anthropicRequest = MessageConverter.ConvertToAnthropicRequest(request);
            anthropicRequest.Stream = true;
            
            // Prepare request
            var httpRequest = await PrepareRequestAsync(anthropicRequest);
            
            // Request prepared for streaming
            
            // Send request
            var response = await _httpClient.SendAsync(
                httpRequest, 
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                var errorMessage = ErrorHandler.ParseErrorMessage(error);
                throw new HttpRequestException($"Anthropic API error ({response.StatusCode}): {errorMessage}");
            }
            
            // Process SSE stream
            var finalResponse = new ChatResponse
            {
                Message = new ChatMessage 
                { 
                    Role = "assistant", 
                    Content = "",
                    ToolCalls = new List<ToolCall>()
                }
            };
            
            using var stream = await response.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(stream);
            
            ToolCall? currentToolCall = null;
            var toolCallJson = "";
            
            while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync();
                
                if (string.IsNullOrEmpty(line))
                    continue;
                    
                if (line.StartsWith("data: "))
                {
                    var data = line.Substring(6);
                    
                    if (data == "[DONE]")
                        break;
                        
                    try
                    {
                        var streamEvent = JsonSerializer.Deserialize<AnthropicStreamEvent>(data);
                        
                        if (streamEvent?.Type == "message_start")
                        {
                            finalResponse.Id = streamEvent.Message?.Id ?? string.Empty;
                            // Preserve the requested model name instead of what the API returns
                            // The API might return the underlying model (e.g., Claude 3.5 Sonnet for Opus 4.1)
                            finalResponse.Model = anthropicRequest.Model;
                        }
                        else if (streamEvent?.Type == "content_block_start")
                        {
                            if (streamEvent.ContentBlock?.Type == "tool_use")
                            {
                                currentToolCall = new ToolCall
                                {
                                    Id = streamEvent.ContentBlock?.Id ?? string.Empty,
                                    Name = streamEvent.ContentBlock?.Name ?? string.Empty,
                                    Arguments = ""
                                };
                                toolCallJson = "";
                            }
                        }
                        else if (streamEvent?.Type == "content_block_delta")
                        {
                            if (streamEvent.Delta?.Type == "text_delta")
                            {
                                var text = streamEvent.Delta?.Text ?? string.Empty;
                                finalResponse.Message.Content += text;
                                
                                await onChunk(new StreamChunk
                                {
                                    Id = finalResponse.Id,
                                    Delta = text,
                                    IsComplete = false
                                });
                            }
                            else if (streamEvent.Delta?.Type == "input_json_delta" && currentToolCall != null)
                            {
                                toolCallJson += streamEvent.Delta?.PartialJson ?? string.Empty;
                            }
                        }
                        else if (streamEvent?.Type == "content_block_stop")
                        {
                            if (currentToolCall != null)
                            {
                                currentToolCall.Arguments = toolCallJson;
                                finalResponse.Message.ToolCalls.Add(currentToolCall);
                                
                                await onChunk(new StreamChunk
                                {
                                    Id = finalResponse.Id,
                                    ToolCall = currentToolCall,
                                    IsComplete = false
                                });
                                
                                currentToolCall = null;
                                toolCallJson = "";
                            }
                        }
                        else if (streamEvent?.Type == "message_delta")
                        {
                            if (streamEvent.Delta?.StopReason != null)
                            {
                                finalResponse.FinishReason = streamEvent.Delta.StopReason;
                            }
                        }
                        else if (streamEvent?.Type == "message_stop")
                        {
                            if (streamEvent.Usage != null)
                            {
                                finalResponse.Usage = new Usage
                                {
                                    InputTokens = streamEvent.Usage.InputTokens,
                                    OutputTokens = streamEvent.Usage.OutputTokens,
                                    TotalTokens = streamEvent.Usage.InputTokens + streamEvent.Usage.OutputTokens
                                };
                            }
                            
                            await onChunk(new StreamChunk
                            {
                                Id = finalResponse.Id,
                                IsComplete = true
                            });
                        }
                    }
                    catch (Exception)
                    {
                        // Error parsing stream event, continue processing
                    }
                }
            }
            
            return finalResponse;
        }
        
        public Task<List<ModelInfo>> GetModelsAsync()
        {
            // Anthropic doesn't have a models endpoint for OAuth users
            // Return hardcoded list of available models
            // TODO: Externalize model catalog to JSON/config or remote source for easy updates of model IDs and pricing without code changes
            return Task.FromResult(new List<ModelInfo>
            {
                new ModelInfo
                {
                    Id = "claude-sonnet-4-20250514",
                    Name = "Claude Sonnet 4",
                    Provider = "Anthropic",
                    MaxTokens = 200000,
                    InputCost = 3, // $3 per 1M input tokens
                    OutputCost = 15, // $15 per 1M output tokens
                    Description = "The latest Claude model with improved reasoning and coding capabilities"
                },
                new ModelInfo
                {
                    Id = "claude-opus-4-1-20250805",
                    Name = "Claude Opus 4.1",
                    Provider = "Anthropic",
                    MaxTokens = 200000,
                    InputCost = 15, // $15 per 1M input tokens
                    OutputCost = 75, // $75 per 1M output tokens
                    Description = "Most capable model for complex analysis, research, and creative tasks"
                }
            });
        }
        
        private async Task<HttpRequestMessage> PrepareRequestAsync(AnthropicChatRequest request)
        {
            // Get valid access token
            var tokens = await _authService.GetValidTokensAsync();
            if (tokens == null)
            {
                throw new InvalidOperationException("No valid authentication tokens available");
            }
            
            // Serialize request with minimal escaping
            var json = JsonSerializer.Serialize(request, new JsonSerializerOptions
            {
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
            
            // Create HTTP request
            var httpRequest = new HttpRequestMessage(HttpMethod.Post, MESSAGES_ENDPOINT)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            
            // Add headers
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
            httpRequest.Headers.Add("anthropic-version", ANTHROPIC_VERSION);
            httpRequest.Headers.Add("anthropic-beta", ANTHROPIC_BETA);
            
            // Add User-Agent header for Claude Code compatibility
            httpRequest.Headers.UserAgent.ParseAdd("Claude-Code/1.0");
            
            // CRITICAL: Remove x-api-key header when using OAuth
            // Anthropic rejects requests that have both Bearer token and x-api-key
            httpRequest.Headers.Remove("x-api-key");
            if (httpRequest.Content != null)
            {
                httpRequest.Content.Headers.Remove("x-api-key");
            }
            
            return httpRequest;
        }
        
        private static bool IsValidRole(string role)
        {
            return role switch
            {
                "system" => true,
                "user" => true,
                "assistant" => true,
                "tool" => true,  // Tool results are converted to user messages by MessageConverter
                _ => false
            };
        }
        
        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}