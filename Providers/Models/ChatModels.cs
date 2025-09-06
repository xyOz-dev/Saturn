using System;
using System.Collections.Generic;
using System.Linq;

namespace Saturn.Providers.Models
{
    public class ChatRequest
    {
        public List<ChatMessage> Messages { get; set; } = new();
        public string Model { get; set; } = string.Empty;
        public double? Temperature { get; set; }
        public int? MaxTokens { get; set; }
        public double? TopP { get; set; }
        public List<ToolDefinition> Tools { get; set; } = new();
        public bool Stream { get; set; }
    }
    
    public class ChatMessage
    {
        public string Role { get; set; } = string.Empty; // "system", "user", "assistant"
        public string Content { get; set; } = string.Empty;
        public List<ToolCall>? ToolCalls { get; set; }
        public string? ToolCallId { get; set; }
    }
    
    public class ChatResponse
    {
        public string Id { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public ChatMessage Message { get; set; } = new();
        public Usage? Usage { get; set; }
        public string? FinishReason { get; set; }
    }
    
    public class StreamChunk
    {
        public string Id { get; set; } = string.Empty;
        public string Delta { get; set; } = string.Empty;
        public ToolCall? ToolCall { get; set; }
        public bool IsComplete { get; set; }
    }
    
    public class Usage
    {
        public int InputTokens { get; set; }
        public int OutputTokens { get; set; }
        public int TotalTokens { get; set; }
    }
    
    public class ModelInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Provider { get; set; } = string.Empty;
        public int MaxTokens { get; set; }
        public double InputCost { get; set; }
        public double OutputCost { get; set; }
        public string Description { get; set; } = string.Empty;
    }
    
    public class ToolDefinition
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public object? Parameters { get; set; } // JSON Schema
    }
    
    public class ToolCall
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Arguments { get; set; } = string.Empty; // JSON string
    }
    
    /// <summary>
    /// Extension methods for validating chat model objects
    /// </summary>
    public static class ChatModelValidations
    {
        private static readonly string[] ValidRoles = { "system", "user", "assistant" };
        
        public static void Validate(this ChatRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            
            if (request.Messages == null)
                throw new ArgumentException("Messages collection cannot be null");
            
            if (request.Messages.Count == 0)
                throw new ArgumentException("Request must contain at least one message");
            
            if (string.IsNullOrEmpty(request.Model))
                throw new ArgumentException("Model name cannot be null or empty");
            
            if (string.IsNullOrWhiteSpace(request.Model))
                throw new ArgumentException("Model name cannot be whitespace only");
            
            // Validate each message
            for (int i = 0; i < request.Messages.Count; i++)
            {
                try
                {
                    request.Messages[i]?.Validate();
                }
                catch (Exception ex)
                {
                    throw new ArgumentException($"Message at index {i} is invalid: {ex.Message}", ex);
                }
            }
            
            // Validate optional parameters
            if (request.Temperature.HasValue && (request.Temperature.Value < 0 || request.Temperature.Value > 2))
                throw new ArgumentException("Temperature must be between 0 and 2");
            
            if (request.MaxTokens.HasValue && request.MaxTokens.Value <= 0)
                throw new ArgumentException("MaxTokens must be greater than 0");
            
            if (request.TopP.HasValue && (request.TopP.Value < 0 || request.TopP.Value > 1))
                throw new ArgumentException("TopP must be between 0 and 1 (inclusive)");
            
            // Validate tools
            if (request.Tools != null)
            {
                for (int i = 0; i < request.Tools.Count; i++)
                {
                    try
                    {
                        request.Tools[i]?.Validate();
                    }
                    catch (Exception ex)
                    {
                        throw new ArgumentException($"Tool definition at index {i} is invalid: {ex.Message}", ex);
                    }
                }
            }
        }
        
        public static void Validate(this ChatMessage message)
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));
            
            if (string.IsNullOrEmpty(message.Role))
                throw new ArgumentException("Message role cannot be null or empty");
            
            if (!ValidRoles.Contains(message.Role.ToLowerInvariant()))
                throw new ArgumentException($"Invalid message role: {message.Role}. Valid roles are: {string.Join(", ", ValidRoles)}");
            
            // Message must have either content or tool calls
            bool hasContent = !string.IsNullOrEmpty(message.Content);
            bool hasToolCalls = message.ToolCalls != null && message.ToolCalls.Count > 0;
            
            if (!hasContent && !hasToolCalls)
                throw new ArgumentException("Message must have either content or tool calls");
            
            // Validate tool calls if present
            if (message.ToolCalls != null)
            {
                for (int i = 0; i < message.ToolCalls.Count; i++)
                {
                    try
                    {
                        message.ToolCalls[i]?.Validate();
                    }
                    catch (Exception ex)
                    {
                        throw new ArgumentException($"Tool call at index {i} is invalid: {ex.Message}", ex);
                    }
                }
            }
            
            // Tool call ID should only be present for tool responses
            if (!string.IsNullOrEmpty(message.ToolCallId) && message.Role.ToLowerInvariant() != "tool")
                throw new ArgumentException("ToolCallId should only be present for tool response messages");
        }
        
        public static void Validate(this ToolDefinition tool)
        {
            if (tool == null)
                throw new ArgumentNullException(nameof(tool));
            
            if (string.IsNullOrEmpty(tool.Name))
                throw new ArgumentException("Tool name cannot be null or empty");
            
            if (string.IsNullOrWhiteSpace(tool.Name))
                throw new ArgumentException("Tool name cannot be whitespace only");
            
            if (string.IsNullOrEmpty(tool.Description))
                throw new ArgumentException("Tool description cannot be null or empty");
            
            if (string.IsNullOrWhiteSpace(tool.Description))
                throw new ArgumentException("Tool description cannot be whitespace only");
            
            // Validate tool name format (should be valid identifier)
            if (!System.Text.RegularExpressions.Regex.IsMatch(tool.Name, @"^[a-zA-Z][a-zA-Z0-9_]*$"))
                throw new ArgumentException("Tool name must be a valid identifier (start with letter, contain only letters, numbers, and underscores)");
        }
        
        public static void Validate(this ToolCall toolCall)
        {
            if (toolCall == null)
                throw new ArgumentNullException(nameof(toolCall));
            
            if (string.IsNullOrEmpty(toolCall.Id))
                throw new ArgumentException("Tool call ID cannot be null or empty");
            
            if (string.IsNullOrWhiteSpace(toolCall.Id))
                throw new ArgumentException("Tool call ID cannot be whitespace only");
            
            if (string.IsNullOrEmpty(toolCall.Name))
                throw new ArgumentException("Tool call name cannot be null or empty");
            
            if (string.IsNullOrWhiteSpace(toolCall.Name))
                throw new ArgumentException("Tool call name cannot be whitespace only");
            
            if (string.IsNullOrEmpty(toolCall.Arguments))
                throw new ArgumentException("Tool call arguments cannot be null or empty");
            
            // Validate tool name format
            if (!System.Text.RegularExpressions.Regex.IsMatch(toolCall.Name, @"^[a-zA-Z][a-zA-Z0-9_]*$"))
                throw new ArgumentException("Tool call name must be a valid identifier");
            
            // Try to validate that arguments is valid JSON
            try
            {
                System.Text.Json.JsonDocument.Parse(toolCall.Arguments);
            }
            catch (Exception ex)
            {
                throw new ArgumentException($"Tool call arguments must be valid JSON: {ex.Message}", ex);
            }
        }
        
        public static void Validate(this ModelInfo model)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));
            
            if (string.IsNullOrEmpty(model.Id))
                throw new ArgumentException("Model ID cannot be null or empty");
            
            if (string.IsNullOrWhiteSpace(model.Id))
                throw new ArgumentException("Model ID cannot be whitespace only");
            
            if (string.IsNullOrEmpty(model.Name))
                throw new ArgumentException("Model name cannot be null or empty");
            
            if (string.IsNullOrWhiteSpace(model.Name))
                throw new ArgumentException("Model name cannot be whitespace only");
            
            if (string.IsNullOrEmpty(model.Provider))
                throw new ArgumentException("Model provider cannot be null or empty");
            
            if (string.IsNullOrWhiteSpace(model.Provider))
                throw new ArgumentException("Model provider cannot be whitespace only");
            
            if (model.MaxTokens <= 0)
                throw new ArgumentException("Model max tokens must be greater than 0");
            
            if (model.InputCost < 0)
                throw new ArgumentException("Model input cost cannot be negative");
            
            if (model.OutputCost < 0)
                throw new ArgumentException("Model output cost cannot be negative");
            
            if (string.IsNullOrEmpty(model.Description))
                throw new ArgumentException("Model description cannot be null or empty");
            
            if (string.IsNullOrWhiteSpace(model.Description))
                throw new ArgumentException("Model description cannot be whitespace only");
        }
    }
}