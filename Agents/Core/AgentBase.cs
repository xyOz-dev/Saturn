using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Saturn.OpenRouter;
using Saturn.OpenRouter.Models.Api.Chat;
using Saturn.OpenRouter.Models.Api.Common;
using Saturn.OpenRouter.Services;
using Saturn.Tools.Core;

namespace Saturn.Agents.Core
{
    public abstract class AgentBase : IStreamingAgent
    {
        public string Id { get; } = Guid.NewGuid().ToString();
        public AgentConfiguration Configuration { get; protected set; }
        public List<Message> ChatHistory { get; protected set; }

        public string Name => Configuration.Name;
        public string SystemPrompt => Configuration.SystemPrompt;

        protected AgentBase(AgentConfiguration configuration)
        {
            Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            ChatHistory = new List<Message>();
            InitializeSystemPrompt();
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
                ChatHistory.Add(new Message { Role = "system", Content = JString(Configuration.SystemPrompt) });
            }
        }

        protected virtual List<Message> PrepareMessages(string userInput)
        {
            var userMessage = new Message { Role = "user", Content = JString(userInput) };

            if (Configuration.MaintainHistory)
            {
                ChatHistory.Add(userMessage);
                TrimHistory();
                return new List<Message>(ChatHistory);
            }

            return new List<Message>
            {
                new Message { Role = "system", Content = JString(Configuration.SystemPrompt) },
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
            }

            return finalMessage;
        }

        public virtual void ClearHistory()
        {
            ChatHistory.Clear();
            InitializeSystemPrompt();
        }

        public virtual List<Message> GetHistory() => new List<Message>(ChatHistory);

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
                    var toolResults = await HandleToolCalls(responseMessage.ToolCalls);

                    // The provider requires that tool result messages are preceded by an assistant message
                    // containing the tool_calls array. Content must be null in that assistant message.
                    var assistantToolCalls = responseMessage.ToolCalls
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
                        // Set content to JSON null
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

                    foreach (var (toolName, toolId, result) in toolResults)
                    {
                        var toolMessage = new Message
                        {
                            Role = "tool",
                            Name = toolName,
                            Content = JString(result.FormattedOutput),
                            ToolCallId = toolId
                        };

                        if (Configuration.MaintainHistory)
                        {
                            ChatHistory.Add(toolMessage);
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

        protected async Task<List<(string toolName, string toolId, ToolResult result)>> HandleToolCalls(ToolCall[] toolCalls)
        {
            var results = new List<(string toolName, string toolId, ToolResult result)>();

            foreach (var toolCall in toolCalls)
            {
                if (toolCall.Type == "function" && toolCall.Function != null)
                {
                    var tool = ToolRegistry.Instance.Get(toolCall.Function.Name);

                    if (tool != null)
                    {
                        try
                        {
                            Dictionary<string, object> parameters;

                            if (string.IsNullOrEmpty(toolCall.Function.Arguments))
                            {
                                parameters = new Dictionary<string, object>();
                            }
                            else
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

                            var toolResult = await tool.ExecuteAsync(parameters);
                            results.Add((toolCall.Function.Name, toolCall.Id, toolResult));
                        }
                        catch (Exception ex)
                        {
                            results.Add((toolCall.Function.Name, toolCall.Id, new ToolResult
                            {
                                Success = false,
                                Error = ex.Message,
                                FormattedOutput = $"Error executing tool: {ex.Message}"
                            }));
                        }
                    }
                    else
                    {
                        results.Add((toolCall.Function.Name, toolCall.Id, new ToolResult
                        {
                            Success = false,
                            Error = $"Tool '{toolCall.Function.Name}' not found",
                            FormattedOutput = $"Tool '{toolCall.Function.Name}' not found in registry"
                        }));
                    }
                }
            }

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
            var toolCallBuffer = new List<ToolCall>();

            while (continueProcessing && !cancellationToken.IsCancellationRequested)
            {
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
                                    var existing = toolCallBuffer.FirstOrDefault(tc => tc.Id == toolCall.Id);
                                    if (existing != null && toolCall.Function?.Arguments != null)
                                    {
                                        // append streamed args
                                        existing.Function!.Arguments = (existing.Function.Arguments ?? string.Empty) + toolCall.Function.Arguments;
                                    }
                                    else
                                    {
                                        toolCallBuffer.Add(toolCall);
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
                    var toolResults = await HandleToolCalls(finalResponse.ToolCalls);

                    // Precede tool_result messages with an assistant message that includes tool_calls
                    var streamedToolCalls = finalResponse.ToolCalls?
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

                    foreach (var (toolName, toolId, result) in toolResults)
                    {
                        var toolMessage = new Message
                        {
                            Role = "tool",
                            Name = toolName,
                            Content = JString(result.FormattedOutput),
                            ToolCallId = toolId
                        };

                        if (Configuration.MaintainHistory)
                        {
                            ChatHistory.Add(toolMessage);
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

        // Helpers

        protected static JsonElement JString(string value)
        {
            return JsonDocument.Parse(JsonSerializer.Serialize(value ?? string.Empty)).RootElement;
        }

        protected static string JsonToString(JsonElement value)
        {
            return value.ValueKind == JsonValueKind.String ? (value.GetString() ?? string.Empty) : value.GetRawText();
        }

    }
}
