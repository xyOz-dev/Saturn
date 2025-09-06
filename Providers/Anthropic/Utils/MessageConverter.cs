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
                // TopP is not set - Anthropic API doesn't allow both temperature and top_p
                // Using temperature only as recommended by Anthropic documentation
                Stream = request.Stream,
                Messages = new List<AnthropicMessage>(),
                Tools = ConvertTools(request.Tools)
            };
            const string requiredPrefix = "You are Claude Code, Anthropic's official CLI for Claude.";
            
            // CRITICAL: OAuth credentials require EXACT system prompt with NO additions
            anthropicRequest.System = requiredPrefix;
            
            // Handle user-provided system messages by converting them to context
            var systemMessage = request.Messages.FirstOrDefault(m => m.Role == "system");
            string? systemInstructions = null;
            
            if (systemMessage != null && !string.IsNullOrWhiteSpace(systemMessage.Content))
            {
                // Extract any user-provided system instructions
                if (!systemMessage.Content.Equals(requiredPrefix, StringComparison.Ordinal))
                {
                    // Remove the required prefix if it's there
                    systemInstructions = systemMessage.Content.Replace(requiredPrefix, "").Trim();
                    if (systemInstructions.StartsWith("\n"))
                        systemInstructions = systemInstructions.Substring(1);
                    
                }
            }
            
            // System prompt set
            
            // Convert other messages, injecting system instructions if needed
            bool firstUserMessage = true;
            foreach (var message in request.Messages.Where(m => m.Role != "system"))
            {
                if (message.Role == "user" && firstUserMessage && !string.IsNullOrWhiteSpace(systemInstructions))
                {
                    // Inject system instructions as context in the first user message
                    var enhancedMessage = ConvertMessage(message);
                    enhancedMessage.Content = $"[Context: {systemInstructions}]\n\n{enhancedMessage.Content}";
                    anthropicRequest.Messages.Add(enhancedMessage);
                    firstUserMessage = false;
                    // System instructions injected into first user message
                }
                else
                {
                    anthropicRequest.Messages.Add(ConvertMessage(message));
                }
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
                ["anthropic/claude-opus-4.1"] = "claude-opus-4-1-20250805",
                ["claude-opus-4.1"] = "claude-opus-4-1-20250805"
            };
            
            return modelMap.TryGetValue(model, out var mapped) ? mapped : model;
        }
        
        private static AnthropicMessage ConvertMessage(ChatMessage message)
        {
            // Tool results must be sent as user messages in Anthropic API
            var role = message.Role;
            if (role == "tool" || !string.IsNullOrEmpty(message.ToolCallId))
            {
                role = "user";
            }
            
            var anthropicMessage = new AnthropicMessage
            {
                Role = role == "user" ? "user" : "assistant"
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