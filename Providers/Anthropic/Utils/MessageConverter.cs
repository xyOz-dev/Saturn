using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Saturn.Providers.Models;
using Saturn.Providers.Anthropic.Models;

namespace Saturn.Providers.Anthropic.Utils
{
    public static class MessageConverter
    {
        public static AnthropicChatRequest ConvertToAnthropicRequest(ChatRequest request)
        {
            var anthropicRequest = new AnthropicChatRequest
            {
                Model = ConvertModelName(request.Model),
                MaxTokens = request.MaxTokens ?? 4096,
                Temperature = request.Temperature,
                TopP = request.TopP,
                Stream = request.Stream,
                Messages = new List<AnthropicMessage>(),
                Tools = ConvertTools(request.Tools)
            };
            
            // Extract system message
            var systemMessage = request.Messages.FirstOrDefault(m => m.Role == "system");
            if (systemMessage != null)
            {
                // Prepend required Claude Code prefix for Anthropic API authentication
                const string requiredPrefix = "You are Claude Code, Anthropic's official CLI for Claude.\n";
                
                // Console logging for debugging
                Console.WriteLine($"[DEBUG] Original system prompt starts with: {systemMessage.Content?.Substring(0, Math.Min(50, systemMessage.Content?.Length ?? 0))}");
                
                // Check if the system prompt already starts with the required prefix
                if (!systemMessage.Content.StartsWith(requiredPrefix, StringComparison.Ordinal))
                {
                    anthropicRequest.System = requiredPrefix + systemMessage.Content;
                    Console.WriteLine($"[DEBUG] Added Claude Code prefix. System prompt now starts with: {anthropicRequest.System?.Substring(0, Math.Min(80, anthropicRequest.System?.Length ?? 0))}");
                }
                else
                {
                    anthropicRequest.System = systemMessage.Content;
                    Console.WriteLine("[DEBUG] Claude Code prefix already present.");
                }
            }
            else
            {
                Console.WriteLine("[DEBUG] No system message found in request!");
            }
            
            // Convert other messages
            foreach (var message in request.Messages.Where(m => m.Role != "system"))
            {
                anthropicRequest.Messages.Add(ConvertMessage(message));
            }
            
            return anthropicRequest;
        }
        
        private static string ConvertModelName(string model)
        {
            // Map common model names to Anthropic format
            var modelMap = new Dictionary<string, string>
            {
                ["claude-sonnet-4"] = "claude-sonnet-4-20250514",
                ["anthropic/claude-sonnet-4"] = "claude-sonnet-4-20250514",
                ["anthropic(claude-opus-4.1"] = "claude-opus-4-1-20250805"
            };
            
            return modelMap.TryGetValue(model, out var mapped) ? mapped : model;
        }
        
        private static AnthropicMessage ConvertMessage(ChatMessage message)
        {
            var anthropicMessage = new AnthropicMessage
            {
                Role = message.Role == "user" ? "user" : "assistant"
            };
            
            // Handle tool calls
            if (message.ToolCalls != null && message.ToolCalls.Any())
            {
                var contentBlocks = new List<ContentBlock>();
                
                // Add text content if present
                if (!string.IsNullOrEmpty(message.Content))
                {
                    contentBlocks.Add(new ContentBlock
                    {
                        Type = "text",
                        Text = message.Content
                    });
                }
                
                // Add tool use blocks
                foreach (var toolCall in message.ToolCalls)
                {
                    contentBlocks.Add(new ContentBlock
                    {
                        Type = "tool_use",
                        Id = toolCall.Id,
                        Name = toolCall.Name,
                        Input = JsonSerializer.Deserialize<object>(toolCall.Arguments)
                    });
                }
                
                anthropicMessage.Content = contentBlocks;
            }
            // Handle tool results
            else if (!string.IsNullOrEmpty(message.ToolCallId))
            {
                anthropicMessage.Content = new List<ContentBlock>
                {
                    new ContentBlock
                    {
                        Type = "tool_result",
                        ToolUseId = message.ToolCallId,
                        ToolResultContent = message.Content
                    }
                };
            }
            // Regular text message
            else
            {
                anthropicMessage.Content = message.Content;
            }
            
            return anthropicMessage;
        }
        
        private static List<AnthropicTool> ConvertTools(List<ToolDefinition> tools)
        {
            if (tools == null || !tools.Any())
                return null;
                
            return tools.Select(tool => new AnthropicTool
            {
                Name = tool.Name,
                Description = tool.Description,
                InputSchema = tool.Parameters
            }).ToList();
        }
        
        public static ChatResponse ConvertFromAnthropicResponse(AnthropicChatResponse response)
        {
            var chatResponse = new ChatResponse
            {
                Id = response.Id,
                Model = response.Model,
                FinishReason = response.StopReason,
                Usage = response.Usage != null ? new Usage
                {
                    InputTokens = response.Usage.InputTokens,
                    OutputTokens = response.Usage.OutputTokens,
                    TotalTokens = response.Usage.InputTokens + response.Usage.OutputTokens
                } : null
            };
            
            // Convert content blocks to message
            var message = new ChatMessage
            {
                Role = "assistant",
                Content = "",
                ToolCalls = new List<ToolCall>()
            };
            
            if (response.Content != null)
            {
                foreach (var block in response.Content)
                {
                    if (block.Type == "text")
                    {
                        message.Content += block.Text;
                    }
                    else if (block.Type == "tool_use")
                    {
                        message.ToolCalls.Add(new ToolCall
                        {
                            Id = block.Id,
                            Name = block.Name,
                            Arguments = JsonSerializer.Serialize(block.Input)
                        });
                    }
                }
            }
            
            chatResponse.Message = message;
            return chatResponse;
        }
    }
}