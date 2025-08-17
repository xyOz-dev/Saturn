using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Saturn.Data;
using Saturn.Data.Models;
using Saturn.OpenRouter;
using Saturn.OpenRouter.Models.Api.Chat;
using Saturn.OpenRouter.Models.Api.Common;
using Saturn.OpenRouter.Services;
using Saturn.Tools.Core;

namespace Saturn.Agents.Core
{
    public abstract class AgentBase
    {
        public string Id { get; } = Guid.NewGuid().ToString();
        public AgentConfiguration Configuration { get; protected set; }
        public List<Message> ChatHistory { get; protected set; }
        public string? CurrentSessionId { get; set; }
        protected ChatHistoryRepository? Repository { get; set; }
        private readonly List<Message> _pendingMessages = new List<Message>();
        
        public event Action<string, string>? OnToolCall;
        
        private class StreamingToolCall
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public JsonStreamAccumulator ArgumentsAccumulator { get; } = new JsonStreamAccumulator();
            public bool IsComplete => ArgumentsAccumulator.IsComplete;
        }

        public string Name => Configuration.Name;
        public string SystemPrompt => Configuration.SystemPrompt;

        protected AgentBase(AgentConfiguration configuration)
        {
            Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            ChatHistory = new List<Message>();
            InitializeSystemPrompt();
            InitializeRepository();
        }

        protected virtual void InitializeRepository()
        {
            try
            {
                Repository = new ChatHistoryRepository();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to initialize chat repository: {ex.Message}");
            }
        }

        public async Task InitializeSessionAsync(string? chatType = "main", string? parentSessionId = null)
        {
            if (Repository != null)
            {
                try
                {
                    var title = $"{Configuration.Name} - {DateTime.Now:yyyy-MM-dd HH:mm}";
                    var session = await Repository.CreateSessionAsync(
                        title,
                        chatType,
                        parentSessionId,
                        Configuration.Name,
                        Configuration.Model,
                        Configuration.SystemPrompt,
                        Configuration.Temperature,
                        Configuration.MaxTokens);
                    CurrentSessionId = session.Id;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Failed to create chat session: {ex.Message}");
                }
            }
        }

        public virtual async Task<T> Execute<T>(object input)
        {
            var messages = PrepareMessages(input?.ToString() ?? string.Empty);
            var responseMessage = await ExecuteWithTools(messages);
            var finalMessage = ProcessResponse(responseMessage);
            return (T)(object)finalMessage;
        }

        public virtual async Task<Message> ExecuteStreamAsync(
            string input,
            Func<StreamChunk, Task> onChunk,
            CancellationToken cancellationToken = default)
        {
            return await ExecuteStreamAsync<Message>(input, onChunk, cancellationToken);
        }

        public virtual async Task<T> ExecuteStreamAsync<T>(
            object input,
            Func<StreamChunk, Task> onChunk,
            CancellationToken cancellationToken = default)
        {
            if (!Configuration.EnableStreaming)
            {
                var result = await Execute<T>(input);
                if (onChunk != null && result is Message msg)
                {
                    await onChunk(new StreamChunk
                    {
                        Content = JsonToString(msg.Content),
                        Role = msg.Role,
                        IsComplete = true
                    });
                }
                return result;
            }

            var messages = PrepareMessages(input?.ToString() ?? string.Empty);
            var responseMessage = await ExecuteWithStreamingTools(messages, onChunk, cancellationToken);
            var finalMessage = ProcessResponse(responseMessage);
            return (T)(object)finalMessage;
        }

        protected virtual void InitializeSystemPrompt()
        {
            if (Configuration.MaintainHistory && !ChatHistory.Any(m => m.Role == "system"))
            {
                ChatHistory.Add(new Message 
                { 
                    Role = "system", 
                    Content = CreateCachedContent(Configuration.SystemPrompt)
                });
            }
        }

        protected virtual List<Message> PrepareMessages(string userInput)
        {
            var userMessage = new Message { Role = "user", Content = JString(userInput) };

            if (Configuration.MaintainHistory)
            {
                ChatHistory.Add(userMessage);
                TrimHistory();
                
                _pendingMessages.Add(userMessage);
                
                return new List<Message>(ChatHistory);
            }

            return new List<Message>
            {
                new Message 
                { 
                    Role = "system", 
                    Content = CreateCachedContent(Configuration.SystemPrompt)
                },
                userMessage
            };
        }

        protected virtual Message ProcessResponse(AssistantMessageResponse responseMessage)
        {
            if (responseMessage == null)
            {
                return new Message { Role = "assistant", Content = JString("I'm sorry, I couldn't process your request.") };
            }

            var finalMessage = new Message
            {
                Role = responseMessage.Role ?? "assistant",
                Content = JString(responseMessage.Content ?? string.Empty)
            };

            if (Configuration.MaintainHistory)
            {
                ChatHistory.Add(finalMessage);
                
                _pendingMessages.Add(finalMessage);
                Task.Run(async () => await FlushPendingMessagesAsync());
            }

            return finalMessage;
        }

        public virtual void ClearHistory()
        {
            ChatHistory.Clear();
            InitializeSystemPrompt();
            
            if (CurrentSessionId != null && Repository != null)
            {
                Task.Run(async () => 
                {
                    await FlushPendingMessagesAsync();
                    await Repository.SetSessionInactiveAsync(CurrentSessionId);
                });
                CurrentSessionId = null;
            }
        }

        public virtual List<Message> GetHistory() => new List<Message>(ChatHistory);

        protected async Task PersistMessageAsync(Message message, CancellationToken cancellationToken = default)
        {
            if (Repository == null || CurrentSessionId == null) return;
            
            try
            {
                await Repository.SaveMessageAsync(CurrentSessionId, message, Configuration.Name, cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to persist message: {ex.Message}");
            }
        }
        
        protected async Task FlushPendingMessagesAsync(CancellationToken cancellationToken = default)
        {
            if (Repository == null || CurrentSessionId == null || _pendingMessages.Count == 0) return;
            
            try
            {
                var messagesToSave = new List<Message>(_pendingMessages);
                _pendingMessages.Clear();
                
                await Repository.SaveMessageBatchAsync(CurrentSessionId, messagesToSave, Configuration.Name, cancellationToken);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to persist message batch: {ex.Message}");
            }
        }

        protected async Task<string?> PersistToolCallAsync(string messageId, string toolName, string arguments)
        {
            if (Repository == null || CurrentSessionId == null) return null;
            
            try
            {
                var toolCall = await Repository.SaveToolCallAsync(messageId, CurrentSessionId, toolName, arguments, Configuration.Name);
                return toolCall.Id;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to persist tool call: {ex.Message}");
                return null;
            }
        }
        
        private string? _currentAssistantMessageId = null;

        protected async Task UpdateToolCallResultAsync(string toolCallId, string? result, string? error, int durationMs)
        {
            if (Repository == null || string.IsNullOrEmpty(toolCallId)) return;
            
            try
            {
                await Repository.UpdateToolCallResultAsync(toolCallId, result, error, durationMs);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to update tool call result: {ex.Message}");
            }
        }

        protected virtual void TrimHistory()
        {
            if (Configuration.MaxHistoryMessages.HasValue && ChatHistory.Count > Configuration.MaxHistoryMessages.Value)
            {
                var systemMessage = ChatHistory.FirstOrDefault(m => m.Role == "system");
                var messagesToKeep = Configuration.MaxHistoryMessages.Value - (systemMessage != null ? 1 : 0);

                var trimmedHistory = ChatHistory
                    .Where(m => m.Role != "system")
                    .TakeLast(messagesToKeep)
                    .ToList();

                ChatHistory.Clear();
                if (systemMessage != null)
                {
                    ChatHistory.Add(systemMessage);
                }
                ChatHistory.AddRange(trimmedHistory);
            }
        }

        protected async Task<AssistantMessageResponse> ExecuteWithTools(List<Message> initialMessages)
        {
            AssistantMessageResponse responseMessage = null;
            var currentMessages = initialMessages;
            bool continueProcessing = true;

            while (continueProcessing)
            {
                if (!ValidateToolMessageSequence(currentMessages))
                {
                    Console.WriteLine("Warning: Invalid tool message sequence detected. Cleaning up messages.");
                    currentMessages = CleanupInvalidToolMessages(currentMessages);
                }
                
                var request = new ChatCompletionRequest
                {
                    Model = Configuration.Model,
                    Messages = currentMessages.ToArray(),
                    Temperature = Configuration.Temperature,
                    MaxTokens = Configuration.MaxTokens,
                    TopP = Configuration.TopP,
                    FrequencyPenalty = Configuration.FrequencyPenalty,
                    PresencePenalty = Configuration.PresencePenalty,
                    Stop = Configuration.StopSequences,
                    Transforms = new string[] { "middle-out" },
                    ToolChoice = ToolChoice.Auto(),
                    Usage = new UsageOption { Include = true }
                };

                if (Configuration.EnableTools)
                {
                    request.Tools = (Configuration.ToolNames != null && Configuration.ToolNames.Count > 0)
                        ? ToolRegistry.Instance.GetOpenRouterToolDefinitions(Configuration.ToolNames.ToArray()).ToArray()
                        : ToolRegistry.Instance.GetOpenRouterToolDefinitions().ToArray();
                }

                var response = await Configuration.Client.Chat.CreateAsync(request);
                responseMessage = response?.Choices?.FirstOrDefault()?.Message;

                if (responseMessage?.ToolCalls != null && responseMessage.ToolCalls.Length > 0)
                {
                    var validToolCalls = responseMessage.ToolCalls
                        .Where(tc => !string.IsNullOrEmpty(tc.Id) && tc.Function != null && !string.IsNullOrEmpty(tc.Function.Name))
                        .ToArray();
                    
                    if (validToolCalls.Length == 0)
                    {
                        continueProcessing = false;
                        continue;
                    }
                    
                    string? assistantMessageId = null;
                    var assistantToolCallsPreview = validToolCalls
                        .Select(tc => new ToolCallRequest
                        {
                            Id = tc.Id,
                            Type = "function",
                            Function = new ToolCallRequest.FunctionCall
                            {
                                Name = tc.Function?.Name,
                                Arguments = tc.Function?.Arguments
                            }
                        })
                        .ToArray();

                    var assistantMessagePreview = new Message
                    {
                        Role = "assistant",
                        Content = JsonDocument.Parse("null").RootElement,
                        ToolCalls = assistantToolCallsPreview
                    };

                    if (Repository != null && CurrentSessionId != null)
                    {
                        try
                        {
                            var savedMessage = await Repository.SaveMessageAsync(CurrentSessionId, assistantMessagePreview, Configuration.Name);
                            assistantMessageId = savedMessage.Id;
                            _currentAssistantMessageId = assistantMessageId;
                        }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine($"Failed to persist assistant message: {ex.Message}");
                        }
                    }

                    var toolResults = await HandleToolCalls(validToolCalls);

                    var processedToolIds = new HashSet<string>(toolResults.Select(r => r.toolId));
                    var assistantToolCalls = validToolCalls
                        .Where(tc => processedToolIds.Contains(tc.Id))
                        .Select(tc => new ToolCallRequest
                        {
                            Id = tc.Id,
                            Type = "function",
                            Function = new ToolCallRequest.FunctionCall
                            {
                                Name = tc.Function?.Name,
                                Arguments = tc.Function?.Arguments
                            }
                        })
                        .ToArray();

                    var assistantMessage = new Message
                    {
                        Role = "assistant",
                        Content = JsonDocument.Parse("null").RootElement,
                        ToolCalls = assistantToolCalls
                    };

                    if (Configuration.MaintainHistory)
                    {
                        ChatHistory.Add(assistantMessage);
                        // Don't add to pending messages since we already saved it
                    }
                    else
                    {
                        currentMessages = new List<Message>(currentMessages) { assistantMessage };
                    }

                    var toolMessages = new List<Message>();
                    foreach (var (toolName, toolId, result) in toolResults)
                    {
                        if (!assistantToolCalls.Any(tc => tc.Id == toolId))
                        {
                            continue;
                        }
                        
                        var toolMessage = new Message
                        {
                            Role = "tool",
                            Name = toolName,
                            Content = JString(result.FormattedOutput),
                            ToolCallId = toolId
                        };
                        
                        toolMessages.Add(toolMessage);
                    }

                    ApplyCacheControlToLargeToolMessages(toolMessages);

                    foreach (var toolMessage in toolMessages)
                    {
                        if (Configuration.MaintainHistory)
                        {
                            ChatHistory.Add(toolMessage);
                            _pendingMessages.Add(toolMessage);
                        }
                        else
                        {
                            currentMessages.Add(toolMessage);
                        }
                    }

                    if (Configuration.MaintainHistory)
                    {
                        currentMessages = new List<Message>(ChatHistory);
                    }
                }
                else
                {
                    continueProcessing = false;
                }
            }

            return responseMessage;
        }

        protected async Task<List<(string toolName, string toolId, ToolResult result)>> HandleToolCalls(OpenRouter.Models.Api.Chat.ToolCall[] toolCalls)
        {
            var results = new List<(string toolName, string toolId, ToolResult result)>();

            foreach (var toolCall in toolCalls)
            {
                if (string.IsNullOrEmpty(toolCall.Id) || toolCall.Function == null || string.IsNullOrEmpty(toolCall.Function.Name))
                {
                    continue;
                }
                
                if (toolCall.Type == "function" && toolCall.Function != null)
                {
                    OnToolCall?.Invoke(toolCall.Function.Name, toolCall.Function.Arguments ?? "{}");
                    
                    // Use the saved assistant message ID if available, otherwise skip persistence
                    var persistedToolCallId = _currentAssistantMessageId != null
                        ? await PersistToolCallAsync(
                            _currentAssistantMessageId, 
                            toolCall.Function.Name, 
                            toolCall.Function.Arguments ?? "{}")
                        : null;
                    
                    var stopwatch = Stopwatch.StartNew();
                    var tool = ToolRegistry.Instance.Get(toolCall.Function.Name);

                    if (tool != null)
                    {
                        try
                        {
                            AgentContext.CurrentConfiguration = Configuration;
                            Dictionary<string, object> parameters;

                            if (string.IsNullOrEmpty(toolCall.Function.Arguments))
                            {
                                parameters = new Dictionary<string, object>();
                            }
                            else
                            {
                                try
                                {
                                    var jsonElement = JsonSerializer.Deserialize<JsonElement>(toolCall.Function.Arguments);
                                    parameters = new Dictionary<string, object>();

                                    if (jsonElement.ValueKind == JsonValueKind.Object)
                                    {
                                        foreach (var property in jsonElement.EnumerateObject())
                                        {
                                            parameters[property.Name] = ConvertJsonElement(property.Value);
                                        }
                                    }
                                }
                                catch (JsonException)
                                {
                                    parameters = new Dictionary<string, object>();
                                }
                            }

                            var toolResult = await tool.ExecuteAsync(parameters);
                            stopwatch.Stop();
                            
                            if (persistedToolCallId != null)
                            {
                                await UpdateToolCallResultAsync(
                                    persistedToolCallId,
                                    toolResult.FormattedOutput,
                                    toolResult.Success ? null : toolResult.Error,
                                    (int)stopwatch.ElapsedMilliseconds);
                            }
                            
                            results.Add((toolCall.Function.Name, toolCall.Id, toolResult));
                        }
                        catch (JsonException jsonEx)
                        {
                            stopwatch.Stop();
                            var errorResult = new ToolResult
                            {
                                Success = false,
                                Error = $"JSON parsing error: {jsonEx.Message}",
                                FormattedOutput = $"Error: Invalid JSON in tool arguments - {jsonEx.Message}"
                            };
                            
                            if (persistedToolCallId != null)
                            {
                                await UpdateToolCallResultAsync(
                                    persistedToolCallId,
                                    null,
                                    errorResult.Error,
                                    (int)stopwatch.ElapsedMilliseconds);
                            }
                            
                            results.Add((toolCall.Function.Name, toolCall.Id, errorResult));
                        }
                        catch (Exception ex)
                        {
                            stopwatch.Stop();
                            var errorResult = new ToolResult
                            {
                                Success = false,
                                Error = ex.Message,
                                FormattedOutput = $"Error executing tool: {ex.Message}"
                            };
                            
                            if (persistedToolCallId != null)
                            {
                                await UpdateToolCallResultAsync(
                                    persistedToolCallId,
                                    null,
                                    errorResult.Error,
                                    (int)stopwatch.ElapsedMilliseconds);
                            }
                            
                            results.Add((toolCall.Function.Name, toolCall.Id, errorResult));
                        }
                    }
                    else
                    {
                        stopwatch.Stop();
                        var errorResult = new ToolResult
                        {
                            Success = false,
                            Error = $"Tool '{toolCall.Function.Name}' not found",
                            FormattedOutput = $"Tool '{toolCall.Function.Name}' not found in registry"
                        };
                        
                        if (persistedToolCallId != null)
                        {
                            await UpdateToolCallResultAsync(
                                persistedToolCallId,
                                null,
                                errorResult.Error,
                                (int)stopwatch.ElapsedMilliseconds);
                        }
                        
                        results.Add((toolCall.Function.Name, toolCall.Id, errorResult));
                    }
                }
            }

            _currentAssistantMessageId = null;
            
            return results;
        }

        protected object ConvertJsonElement(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.String:
                    return element.GetString();
                case JsonValueKind.Number:
                    if (element.TryGetInt32(out int intValue))
                        return intValue;
                    if (element.TryGetInt64(out long longValue))
                        return longValue;
                    if (element.TryGetDouble(out double doubleValue))
                        return doubleValue;
                    return element.GetDecimal();
                case JsonValueKind.True:
                    return true;
                case JsonValueKind.False:
                    return false;
                case JsonValueKind.Null:
                    return null;
                case JsonValueKind.Array:
                    var list = new List<object>();
                    foreach (var item in element.EnumerateArray())
                    {
                        list.Add(ConvertJsonElement(item));
                    }
                    return list;
                case JsonValueKind.Object:
                    var dict = new Dictionary<string, object>();
                    foreach (var property in element.EnumerateObject())
                    {
                        dict[property.Name] = ConvertJsonElement(property.Value);
                    }
                    return dict;
                default:
                    return element.ToString();
            }
        }

        protected async Task<AssistantMessageResponse> ExecuteWithStreamingTools(
            List<Message> initialMessages,
            Func<StreamChunk, Task> onChunk,
            CancellationToken cancellationToken)
        {
            AssistantMessageResponse finalResponse = null;
            var currentMessages = initialMessages;
            bool continueProcessing = true;
            var contentBuffer = new StringBuilder();
            var toolCallBuffer = new List<OpenRouter.Models.Api.Chat.ToolCall>();

            while (continueProcessing && !cancellationToken.IsCancellationRequested)
            {
                if (!ValidateToolMessageSequence(currentMessages))
                {
                    Console.WriteLine("Warning: Invalid tool message sequence detected. Cleaning up messages.");
                    currentMessages = CleanupInvalidToolMessages(currentMessages);
                }

                var request = new ChatCompletionRequest
                {
                    Model = Configuration.Model,
                    Messages = currentMessages.ToArray(),
                    Temperature = Configuration.Temperature,
                    MaxTokens = Configuration.MaxTokens,
                    TopP = Configuration.TopP,
                    FrequencyPenalty = Configuration.FrequencyPenalty,
                    PresencePenalty = Configuration.PresencePenalty,
                    Stop = Configuration.StopSequences,
                    Transforms = new string[] { "middle-out" },
                    ToolChoice = ToolChoice.Auto(),
                    Stream = true,
                    Usage = new UsageOption { Include = true }
                };

                if (Configuration.EnableTools)
                {
                    request.Tools = (Configuration.ToolNames != null && Configuration.ToolNames.Count > 0)
                        ? ToolRegistry.Instance.GetOpenRouterToolDefinitions(Configuration.ToolNames.ToArray()).ToArray()
                        : ToolRegistry.Instance.GetOpenRouterToolDefinitions().ToArray();
                }

                try
                {
                    var tokenIndex = 0;

                    await foreach (var chunk in Configuration.Client.ChatStreaming.StreamAsync(request, cancellationToken))
                    {
                        if (cancellationToken.IsCancellationRequested)
                            break;

                        var choice = chunk.Choices?.FirstOrDefault();
                        if (choice?.Delta != null)
                        {
                            var delta = choice.Delta;

                            if (!string.IsNullOrEmpty(delta.Content))
                            {
                                var contentStr = delta.Content;
                                contentBuffer.Append(contentStr);
                                await onChunk(new StreamChunk
                                {
                                    Content = contentStr,
                                    Role = delta.Role ?? "assistant",
                                    IsToolCall = false,
                                    TokenIndex = tokenIndex++
                                });
                            }

                            if (delta.ToolCalls != null && delta.ToolCalls.Length > 0)
                            {
                                foreach (var toolCall in delta.ToolCalls)
                                {
                                    ToolCall existing = null;
                                    
                                    if (toolCall.Index.HasValue)
                                    {
                                        while (toolCallBuffer.Count <= toolCall.Index.Value)
                                        {
                                            toolCallBuffer.Add(new ToolCall { Function = new ToolCall.FunctionCall() });
                                        }
                                        existing = toolCallBuffer[toolCall.Index.Value];
                                    }
                                    else if (!string.IsNullOrEmpty(toolCall.Id))
                                    {
                                        existing = toolCallBuffer.FirstOrDefault(tc => tc.Id == toolCall.Id);
                                        if (existing == null)
                                        {
                                            existing = new ToolCall
                                            {
                                                Id = toolCall.Id,
                                                Type = toolCall.Type ?? "function",
                                                Function = new ToolCall.FunctionCall()
                                            };
                                            toolCallBuffer.Add(existing);
                                        }
                                    }
                                    else
                                    {
                                        continue;
                                    }
                                    
                                    if (existing.Function == null)
                                    {
                                        existing.Function = new ToolCall.FunctionCall();
                                    }
                                    
                                    if (!string.IsNullOrEmpty(toolCall.Id))
                                    {
                                        existing.Id = toolCall.Id;
                                    }
                                    
                                    if (!string.IsNullOrEmpty(toolCall.Function?.Name))
                                    {
                                        existing.Function.Name = toolCall.Function.Name;
                                    }
                                    
                                    if (toolCall.Function?.Arguments != null)
                                    {
                                        existing.Function.Arguments = (existing.Function.Arguments ?? string.Empty) + toolCall.Function.Arguments;
                                    }

                                    await onChunk(new StreamChunk
                                    {
                                        IsToolCall = true,
                                        ToolCallId = toolCall.Id,
                                        ToolName = toolCall.Function?.Name,
                                        ToolArguments = toolCall.Function?.Arguments,
                                        TokenIndex = tokenIndex++
                                    });
                                }
                            }
                        }

                        if (!string.IsNullOrEmpty(choice?.FinishReason))
                        {
                            finalResponse = new AssistantMessageResponse
                            {
                                Role = "assistant",
                                Content = contentBuffer.ToString(),
                                ToolCalls = toolCallBuffer.Count > 0 ? toolCallBuffer.ToArray() : null
                            };
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    await onChunk(new StreamChunk
                    {
                        IsComplete = true,
                        Content = "[Stream cancelled]"
                    });
                    throw;
                }

                if (finalResponse?.ToolCalls != null && finalResponse.ToolCalls.Length > 0)
                {
                    var validatedToolCalls = ValidateStreamedToolCalls(finalResponse.ToolCalls);
                    if (validatedToolCalls.Count == 0)
                    {
                        continueProcessing = false;
                        continue;
                    }
                    
                    var streamedToolCallsPreview = validatedToolCalls
                        .Select(tc => new ToolCallRequest
                        {
                            Id = tc.Id,
                            Type = "function",
                            Function = new ToolCallRequest.FunctionCall
                            {
                                Name = tc.Function?.Name,
                                Arguments = tc.Function?.Arguments
                            }
                        })
                        .ToArray();

                    var assistantMessagePreview = new Message
                    {
                        Role = "assistant",
                        Content = JsonDocument.Parse("null").RootElement,
                        ToolCalls = streamedToolCallsPreview
                    };

                    if (Repository != null && CurrentSessionId != null)
                    {
                        try
                        {
                            var savedMessage = await Repository.SaveMessageAsync(CurrentSessionId, assistantMessagePreview, Configuration.Name);
                            _currentAssistantMessageId = savedMessage.Id;
                        }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine($"Failed to persist assistant message: {ex.Message}");
                        }
                    }

                    var toolResults = await HandleToolCalls(validatedToolCalls.ToArray());

                    var processedToolIds = new HashSet<string>(toolResults.Select(r => r.toolId));
                    var streamedToolCalls = validatedToolCalls
                        .Where(tc => processedToolIds.Contains(tc.Id))
                        .Select(tc => new ToolCallRequest
                        {
                            Id = tc.Id,
                            Type = "function",
                            Function = new ToolCallRequest.FunctionCall
                            {
                                Name = tc.Function?.Name,
                                Arguments = tc.Function?.Arguments
                            }
                        })
                        .ToArray();

                    var assistantMessage = new Message
                    {
                        Role = "assistant",
                        Content = JsonDocument.Parse("null").RootElement,
                        ToolCalls = streamedToolCalls
                    };

                    if (Configuration.MaintainHistory)
                    {
                        ChatHistory.Add(assistantMessage);
                    }
                    else
                    {
                        currentMessages = new List<Message>(currentMessages) { assistantMessage };
                    }

                    var toolMessages = new List<Message>();
                    foreach (var (toolName, toolId, result) in toolResults)
                    {
                        if (!streamedToolCalls.Any(tc => tc.Id == toolId))
                        {
                            continue;
                        }
                        
                        var toolMessage = new Message
                        {
                            Role = "tool",
                            Name = toolName,
                            Content = JString(result.FormattedOutput),
                            ToolCallId = toolId
                        };
                        
                        toolMessages.Add(toolMessage);
                    }

                    ApplyCacheControlToLargeToolMessages(toolMessages);

                    foreach (var toolMessage in toolMessages)
                    {
                        if (Configuration.MaintainHistory)
                        {
                            ChatHistory.Add(toolMessage);
                            _pendingMessages.Add(toolMessage);
                        }
                        else
                        {
                            currentMessages.Add(toolMessage);
                        }
                    }

                    if (Configuration.MaintainHistory)
                    {
                        currentMessages = new List<Message>(ChatHistory);
                    }

                    contentBuffer.Clear();
                    toolCallBuffer.Clear();
                }
                else
                {
                    continueProcessing = false;
                }
            }

            await onChunk(new StreamChunk { IsComplete = true });
            return finalResponse;
        }

        protected static JsonElement JString(string value)
        {
            return JsonDocument.Parse(JsonSerializer.Serialize(value ?? string.Empty)).RootElement;
        }

        protected static string JsonToString(JsonElement value)
        {
            return value.ValueKind == JsonValueKind.String ? (value.GetString() ?? string.Empty) : value.GetRawText();
        }

        /// <summary>
        /// Validates streamed tool calls to ensure they have required fields
        /// </summary>
        private List<OpenRouter.Models.Api.Chat.ToolCall> ValidateStreamedToolCalls(OpenRouter.Models.Api.Chat.ToolCall[] toolCalls)
        {
            var validatedCalls = new List<OpenRouter.Models.Api.Chat.ToolCall>();
            
            foreach (var toolCall in toolCalls)
            {
                if (string.IsNullOrEmpty(toolCall.Id))
                {
                    continue;
                }
                
                if (toolCall.Function == null || string.IsNullOrEmpty(toolCall.Function.Name))
                {
                    continue;
                }
                
                if (toolCall.Function.Arguments == null)
                {
                    toolCall.Function.Arguments = "{}";
                }
                
                validatedCalls.Add(toolCall);
            }
            
            return validatedCalls;
        }
        
        /// <summary>
        /// Validates message sequence to ensure tool use/result pairing for Bedrock compatibility
        /// </summary>
        protected bool ValidateToolMessageSequence(List<Message> messages)
        {
            var toolUseIds = new HashSet<string>();
            var toolResultIds = new HashSet<string>();
            
            foreach (var message in messages)
            {
                if (message.Role == "assistant" && message.ToolCalls != null)
                {
                    foreach (var toolCall in message.ToolCalls)
                    {
                        if (!string.IsNullOrEmpty(toolCall.Id))
                        {
                            toolUseIds.Add(toolCall.Id);
                        }
                    }
                }
                else if (message.Role == "tool" && !string.IsNullOrEmpty(message.ToolCallId))
                {
                    toolResultIds.Add(message.ToolCallId);
                }
            }
            
            foreach (var resultId in toolResultIds)
            {
                if (!toolUseIds.Contains(resultId))
                {
                    Console.WriteLine($"Warning: Tool result with ID {resultId} has no corresponding tool use");
                    return false;
                }
            }
            
            return true;
        }
        
        /// <summary>
        /// Cleans up invalid tool messages to ensure proper pairing for Bedrock compatibility
        /// </summary>
        protected List<Message> CleanupInvalidToolMessages(List<Message> messages)
        {
            var cleanedMessages = new List<Message>();
            var validToolUseIds = new HashSet<string>();
            
            foreach (var message in messages)
            {
                if (message.Role == "assistant" && message.ToolCalls != null)
                {
                    foreach (var toolCall in message.ToolCalls)
                    {
                        if (!string.IsNullOrEmpty(toolCall.Id))
                        {
                            validToolUseIds.Add(toolCall.Id);
                        }
                    }
                }
            }
            
            foreach (var message in messages)
            {
                if (message.Role == "tool")
                {
                    if (!string.IsNullOrEmpty(message.ToolCallId) && validToolUseIds.Contains(message.ToolCallId))
                    {
                        cleanedMessages.Add(message);
                    }
                    else
                    {
                        Console.WriteLine($"Removing orphaned tool result message with ID: {message.ToolCallId}");
                    }
                }
                else if (message.Role == "assistant" && message.ToolCalls != null)
                {
                    var validToolCalls = message.ToolCalls
                        .Where(tc => !string.IsNullOrEmpty(tc.Id) && tc.Function != null && !string.IsNullOrEmpty(tc.Function?.Name))
                        .ToArray();
                    
                    if (validToolCalls.Length > 0)
                    {
                        message.ToolCalls = validToolCalls;
                        cleanedMessages.Add(message);
                    }
                    else if (message.Content.ValueKind != JsonValueKind.Null)
                    {
                        message.ToolCalls = null;
                        cleanedMessages.Add(message);
                    }
                }
                else
                {
                    cleanedMessages.Add(message);
                }
            }
            
            return cleanedMessages;
        }


        private void ApplyCacheControlToLargeToolMessages(List<Message> toolMessages)
        {
            if (!Configuration.Model.StartsWith("anthropic", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var largeToolMessages = toolMessages
                .Select((msg, index) => new { Message = msg, Index = index, Length = GetContentLength(msg) })
                .Where(x => x.Length > 10000) 
                .OrderByDescending(x => x.Length)
                .Take(3)
                .Select(x => x.Index)
                .ToHashSet();

            for (int i = 0; i < toolMessages.Count; i++)
            {
                if (largeToolMessages.Contains(i))
                {
                    var content = GetContentString(toolMessages[i]);
                    if (!string.IsNullOrEmpty(content))
                    {
                        toolMessages[i].Content = CreateCachedContent(content);
                    }
                }
            }
        }

        private int GetContentLength(Message message)
        {
            try
            {
                if (message.Content.ValueKind == JsonValueKind.String)
                {
                    return message.Content.GetString()?.Length ?? 0;
                }
                else if (message.Content.ValueKind == JsonValueKind.Array)
                {
                    int totalLength = 0;
                    foreach (var element in message.Content.EnumerateArray())
                    {
                        if (element.TryGetProperty("text", out var textProp))
                        {
                            totalLength += textProp.GetString()?.Length ?? 0;
                        }
                    }
                    return totalLength;
                }
                return 0;
            }
            catch
            {
                return 0;
            }
        }

        private string? GetContentString(Message message)
        {
            try
            {
                if (message.Content.ValueKind == JsonValueKind.String)
                {
                    return message.Content.GetString();
                }
                else if (message.Content.ValueKind == JsonValueKind.Array)
                {
                    var texts = new List<string>();
                    foreach (var element in message.Content.EnumerateArray())
                    {
                        if (element.TryGetProperty("text", out var textProp))
                        {
                            var text = textProp.GetString();
                            if (!string.IsNullOrEmpty(text))
                            {
                                texts.Add(text);
                            }
                        }
                    }
                    return string.Join("", texts);
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        private JsonElement CreateCachedContent(string text)
        {
            var contentParts = new[]
            {
                new TextContentPart
                {
                    Type = "text",
                    Text = text,
                    CacheControl = new CacheControl { Type = "ephemeral" }
                }
            };

            var json = JsonSerializer.Serialize(contentParts);
            return JsonDocument.Parse(json).RootElement;
        }

    }
}
