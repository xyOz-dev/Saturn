using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Saturn.Agents.Core.Objects;
using Saturn.Data;
using Saturn.Data.Models;
using Saturn.OpenRouter;
using Saturn.OpenRouter.Models.Api.Chat;
using Saturn.OpenRouter.Models.Api.Common;
using Saturn.OpenRouter.Services;
using Saturn.Providers;
using Saturn.Tools.Core;

namespace Saturn.Agents.Core
{
    public abstract class AgentBase : IDisposable
    {
        public string Id { get; } = Guid.NewGuid().ToString();
        public AgentConfiguration Configuration { get; protected set; }
        public List<Message> ChatHistory { get; protected set; }
        public string? CurrentSessionId { get; set; }
        public string? ManagerAgentId { get; set; }
        public bool IsOrchestrator { get; set; }
        protected ChatHistoryRepository? Repository { get; set; }
        private readonly List<(string SessionId, Message Message)> _pendingMessages = new();
        private readonly object _pendingMessagesLock = new object();
        private readonly List<Task> _pendingFlushTasks = new();
        
        public event Action<string, string>? OnToolCall;

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

        public void RehydrateHistory(IReadOnlyList<(string Role, string Content)> messages)
        {
            if (!Configuration.MaintainHistory)
            {
                return;
            }

            foreach (var (role, content) in messages)
            {
                if (role != "user" && role != "assistant")
                {
                    continue;
                }
                ChatHistory.Add(new Message { Role = role, Content = JString(content) });
            }
            TrimHistory();
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

                EnqueuePendingMessage(userMessage);

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

                EnqueuePendingMessage(finalMessage);
                var sessionId = CurrentSessionId;
                TrackFlushTask(Task.Run(() => FlushPendingMessagesAsync(sessionId, CancellationToken.None)));
            }

            return finalMessage;
        }

        public virtual void ClearHistory()
        {
            ChatHistory.Clear();
            InitializeSystemPrompt();

            var sessionId = CurrentSessionId;
            CurrentSessionId = null;

            if (sessionId != null && Repository != null)
            {
                var repository = Repository;
                TrackFlushTask(Task.Run(async () =>
                {
                    await FlushPendingMessagesAsync(sessionId, CancellationToken.None);
                    await repository.SetSessionInactiveAsync(sessionId);
                }));
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
        
        protected Task FlushPendingMessagesAsync(CancellationToken cancellationToken = default)
        {
            return FlushPendingMessagesAsync(CurrentSessionId, cancellationToken);
        }

        private async Task FlushPendingMessagesAsync(string? sessionId, CancellationToken cancellationToken)
        {
            if (Repository == null || sessionId == null) return;

            // Only drain this session's messages; a turn still running for a cleared
            // session must not leak its messages into the next session's flush.
            List<Message> messagesToSave;
            lock (_pendingMessagesLock)
            {
                messagesToSave = _pendingMessages
                    .Where(p => p.SessionId == sessionId)
                    .Select(p => p.Message)
                    .ToList();
                if (messagesToSave.Count == 0) return;
                _pendingMessages.RemoveAll(p => p.SessionId == sessionId);
            }

            try
            {
                await Repository.SaveMessageBatchAsync(sessionId, messagesToSave, Configuration.Name, cancellationToken);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to persist message batch: {ex.Message}");
            }
        }

        private void EnqueuePendingMessage(Message message)
        {
            var sessionId = CurrentSessionId;
            if (sessionId == null) return;

            lock (_pendingMessagesLock)
            {
                _pendingMessages.Add((sessionId, message));
            }
        }

        private void TrackFlushTask(Task task)
        {
            lock (_pendingMessagesLock)
            {
                _pendingFlushTasks.RemoveAll(t => t.IsCompleted);
                _pendingFlushTasks.Add(task);
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
            var client = Configuration.ClientSource.Current;

            AssistantMessageResponse responseMessage = null;
            var currentMessages = initialMessages;
            bool continueProcessing = true;

            while (continueProcessing)
            {
                if (!ValidateToolMessageSequence(currentMessages))
                {

                    currentMessages = CleanupInvalidToolMessages(currentMessages);
                }

                var request = BuildRequest(client, currentMessages);

                var response = await client.ChatAsync(request);
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

                    foreach (var toolCall in validToolCalls)
                    {
                        toolCall.Function!.Arguments = EnsureValidJsonArguments(toolCall.Function.Arguments);
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
                            EnqueuePendingMessage(toolMessage);
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
                            AgentContext.Current = new AgentExecutionContext
                            {
                                Configuration = Configuration,
                                AgentInstanceId = Id,
                                SessionId = CurrentSessionId,
                                ManagerAgentId = ManagerAgentId,
                                AgentName = Name,
                                IsOrchestrator = IsOrchestrator
                            };
                            var parameters = new Dictionary<string, object>();
                            string? argumentError = null;

                            if (!string.IsNullOrEmpty(toolCall.Function.Arguments))
                            {
                                try
                                {
                                    var jsonElement = JsonSerializer.Deserialize<JsonElement>(toolCall.Function.Arguments);

                                    if (jsonElement.ValueKind == JsonValueKind.Object)
                                    {
                                        foreach (var property in jsonElement.EnumerateObject())
                                        {
                                            parameters[property.Name] = ConvertJsonElement(property.Value);
                                        }
                                    }
                                    else
                                    {
                                        argumentError = $"Tool arguments must be a JSON object, but got {jsonElement.ValueKind}.";
                                    }
                                }
                                catch (JsonException jsonEx)
                                {
                                    argumentError = $"Tool arguments were not valid JSON: {jsonEx.Message}";
                                }
                            }

                            argumentError ??= ValidateToolArguments(tool, parameters);

                            if (argumentError != null)
                            {
                                stopwatch.Stop();
                                var invalidArgsResult = new ToolResult
                                {
                                    Success = false,
                                    Error = argumentError,
                                    FormattedOutput = $"Error: {argumentError} Call the tool again with corrected arguments."
                                };

                                if (persistedToolCallId != null)
                                {
                                    await UpdateToolCallResultAsync(
                                        persistedToolCallId,
                                        null,
                                        invalidArgsResult.Error,
                                        (int)stopwatch.ElapsedMilliseconds);
                                }

                                results.Add((toolCall.Function.Name, toolCall.Id, invalidArgsResult));
                                continue;
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

        private static string? ValidateToolArguments(ITool tool, Dictionary<string, object> parameters)
        {
            Dictionary<string, object>? schema;
            try
            {
                schema = tool.GetParameters();
            }
            catch
            {
                return null;
            }

            if (schema == null ||
                !schema.TryGetValue("properties", out var propertiesObj) ||
                propertiesObj is not Dictionary<string, object> properties)
            {
                return null;
            }

            var errors = new List<string>();

            if (schema.TryGetValue("required", out var requiredObj))
            {
                var requiredNames = requiredObj switch
                {
                    string[] array => array,
                    IEnumerable<string> sequence => sequence.ToArray(),
                    _ => Array.Empty<string>()
                };

                var missing = requiredNames
                    .Where(name => !string.IsNullOrEmpty(name) && !parameters.ContainsKey(name))
                    .ToList();

                if (missing.Count > 0)
                {
                    errors.Add($"Missing required parameter{(missing.Count > 1 ? "s" : "")}: {string.Join(", ", missing.Select(name => $"'{name}'"))}.");
                }
            }

            var unknown = parameters.Keys.Where(key => !properties.ContainsKey(key)).ToList();
            if (unknown.Count > 0)
            {
                errors.Add($"Unknown parameter{(unknown.Count > 1 ? "s" : "")}: {string.Join(", ", unknown.Select(name => $"'{name}'"))}. Valid parameters for {tool.Name}: {string.Join(", ", properties.Keys)}.");
            }

            return errors.Count > 0 ? string.Join(" ", errors) : null;
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
            var client = Configuration.ClientSource.Current;

            AssistantMessageResponse finalResponse = null;
            var currentMessages = initialMessages;
            bool continueProcessing = true;
            var contentBuffer = new StringBuilder();
            var toolCallBuffer = new List<OpenRouter.Models.Api.Chat.ToolCall>();

            while (continueProcessing && !cancellationToken.IsCancellationRequested)
            {
                if (!ValidateToolMessageSequence(currentMessages))
                {
                    currentMessages = CleanupInvalidToolMessages(currentMessages);
                }

                var request = BuildRequest(client, currentMessages);
                request.Stream = true;

                try
                {
                    var tokenIndex = 0;

                    await foreach (var chunk in client.StreamChatAsync(request, cancellationToken))
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
                            if (string.Equals(choice.FinishReason, "length", StringComparison.OrdinalIgnoreCase))
                            {
                                await onChunk(new StreamChunk
                                {
                                    Content = "\n[Response truncated: max token limit reached. Consider raising Max Tokens, especially for reasoning models.]\n",
                                    Role = "assistant"
                                });
                            }

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
                catch (Saturn.OpenRouter.Errors.OpenRouterException ex)
                {
                    var msg = ex.Message ?? string.Empty;
                    var looksLikeStreamingNotAllowed =
                        msg.IndexOf("param", StringComparison.OrdinalIgnoreCase) >= 0 &&
                        msg.IndexOf("stream", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        msg.IndexOf("unsupported", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        msg.IndexOf("must be verified", StringComparison.OrdinalIgnoreCase) >= 0;

                    if (looksLikeStreamingNotAllowed)
                    {
                        await onChunk(new StreamChunk { Content = "[Provider rejected streaming; falling back to non-streaming]", Role = "assistant" });
                        finalResponse = await ExecuteWithTools(currentMessages);
                        contentBuffer.Clear();
                        toolCallBuffer.Clear();
                        continueProcessing = false;
                    }
                    else
                    {
                        throw;
                    }
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
                            EnqueuePendingMessage(toolMessage);
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

        private ChatCompletionRequest BuildRequest(ILlmClient client, List<Message> currentMessages)
        {
            var capabilities = client.Capabilities;

            var messages = capabilities.SupportsCaching
                ? currentMessages.ToArray()
                : currentMessages.Select(StripCacheControl).ToArray();

            var request = new ChatCompletionRequest
            {
                Model = Configuration.Model,
                Messages = messages,
                Temperature = Configuration.Temperature,
                MaxTokens = Configuration.MaxTokens,
                TopP = Configuration.TopP,
                FrequencyPenalty = Configuration.FrequencyPenalty,
                PresencePenalty = Configuration.PresencePenalty,
                Stop = Configuration.StopSequences,
                Transforms = capabilities.SupportsTransforms ? new string[] { "middle-out" } : null,
                Usage = capabilities.SupportsUsageInclude ? new UsageOption { Include = true } : null
            };

            if (Configuration.EnableTools)
            {
                request.Tools = (Configuration.ToolNames != null && Configuration.ToolNames.Count > 0)
                    ? ToolRegistry.Instance.GetOpenRouterToolDefinitions(Configuration.ToolNames.ToArray()).ToArray()
                    : ToolRegistry.Instance.GetOpenRouterToolDefinitions().ToArray();

                if (capabilities.SupportsToolChoice)
                {
                    request.ToolChoice = ToolChoice.Auto();
                }
            }

            return request;
        }

        private static string EnsureValidJsonArguments(string? arguments)
        {
            if (string.IsNullOrWhiteSpace(arguments))
            {
                return "{}";
            }

            try
            {
                using var _ = JsonDocument.Parse(arguments);
                return arguments;
            }
            catch (JsonException)
            {
                return "{}";
            }
        }

        private static Message StripCacheControl(Message message)
        {
            if (message.Content.ValueKind != JsonValueKind.Array)
            {
                return message;
            }

            var hasCacheControl = false;
            var texts = new List<string>();
            foreach (var part in message.Content.EnumerateArray())
            {
                if (part.ValueKind != JsonValueKind.Object || !part.TryGetProperty("text", out var textProp))
                {
                    return message;
                }
                if (part.TryGetProperty("cache_control", out _))
                {
                    hasCacheControl = true;
                }
                texts.Add(textProp.GetString() ?? string.Empty);
            }

            if (!hasCacheControl)
            {
                return message;
            }

            return new Message
            {
                Role = message.Role,
                Content = JString(string.Join("", texts)),
                Name = message.Name,
                ToolCallId = message.ToolCallId,
                ToolCalls = message.ToolCalls
            };
        }

        protected static JsonElement JString(string value)
        {
            return JsonDocument.Parse(JsonSerializer.Serialize(value ?? string.Empty)).RootElement;
        }

        protected static string JsonToString(JsonElement value)
        {
            return value.ValueKind == JsonValueKind.String ? (value.GetString() ?? string.Empty) : value.GetRawText();
        }

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

                toolCall.Function.Arguments = EnsureValidJsonArguments(toolCall.Function.Arguments);

                validatedCalls.Add(toolCall);
            }
            
            return validatedCalls;
        }
        
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
                    return false;
                }
            }
            
            return true;
        }
        
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
                        if (System.Diagnostics.Debugger.IsAttached)
                        {
                            Console.Error.WriteLine($"Skipping orphaned tool result message with ID: {message.ToolCallId ?? "(null)"}");
                        }
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
            if (!ProviderSupportsCaching())
            {
                return JString(text);
            }

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

        private bool ProviderSupportsCaching()
        {
            try
            {
                return Configuration.ClientSource.Current.Capabilities.SupportsCaching;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                Task[] pendingFlushes;
                lock (_pendingMessagesLock)
                {
                    pendingFlushes = _pendingFlushTasks.Where(t => !t.IsCompleted).ToArray();
                    _pendingFlushTasks.Clear();
                }

                if (pendingFlushes.Length > 0)
                {
                    // Give queued persistence work a chance to finish before the
                    // repository goes away; flush failures log rather than throw.
                    try { Task.WaitAll(pendingFlushes, TimeSpan.FromSeconds(5)); } catch { }
                }

                Repository?.Dispose();
                Repository = null;
            }
        }

    }
}
