using OpenRouterSharp;
using OpenRouterSharp.Models.Requests;
using OpenRouterSharp.Models.Responses;
using Saturn.Tools.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Saturn.Agents.Core
{
    public abstract class AgentBase
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
        }

        public abstract Task<T> Execute<T>(object input);

        public virtual void ClearHistory()
        {
            ChatHistory.Clear();
        }

        public virtual List<Message> GetHistory()
        {
            return new List<Message>(ChatHistory);
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

        protected async Task<ResponseMessage> ExecuteWithTools(List<Message> initialMessages)
        {
            ResponseMessage responseMessage = null;
            var currentMessages = initialMessages;
            bool continueProcessing = true;
            
            while (continueProcessing)
            {
                var request = new ChatRequest
                {
                    Model = Configuration.Model,
                    Messages = currentMessages,
                    Temperature = Configuration.Temperature,
                    MaxTokens = Configuration.MaxTokens,
                    TopP = Configuration.TopP,
                    FrequencyPenalty = Configuration.FrequencyPenalty,
                    PresencePenalty = Configuration.PresencePenalty,
                    Stop = Configuration.StopSequences,
                    ToolChoice = "auto"
                };
                
                if (Configuration.EnableTools && Configuration.ToolNames?.Count > 0)
                {
                    request.Tools = ToolRegistry.Instance.GetOpenRouterTools(Configuration.ToolNames.ToArray());
                }
                
                var response = await Configuration.Client.Chat.CreateCompletionAsync(request);
                responseMessage = response.Choices.FirstOrDefault()?.Message;
                
                if (responseMessage != null && responseMessage.ToolCalls != null && responseMessage.ToolCalls.Count > 0)
                {
                    var toolResults = await HandleToolCalls(responseMessage.ToolCalls);
                    
                    var assistantMessage = new Message
                    {
                        Role = "assistant",
                        Content = responseMessage.Content?.ToString() ?? "",
                        ToolCalls = responseMessage.ToolCalls
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
                            Content = result.FormattedOutput,
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
        
        protected async Task<List<(string toolName, string toolId, ToolResult result)>> HandleToolCalls(List<ToolCall> toolCalls)
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
    }
}
