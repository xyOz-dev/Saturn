using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Saturn.Agents;
using Saturn.Agents.Core;
using OpenRouterSharp.Models.Responses;
using OpenRouterSharp.Models.Requests;

namespace Saturn.Web
{
    public class ApiHandler
    {
        private static DefaultAgent _sharedAgent;
        private static readonly object _agentLock = new object();

        public async Task HandleRequest(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;
            var path = request.Url.AbsolutePath;

            try
            {
                switch (path)
                {
                    case "/api/chat":
                        if (request.HttpMethod == "POST")
                        {
                            await HandleChatRequest(context);
                        }
                        else
                        {
                            await SendJsonResponse(response, 405, new { error = "Method not allowed" });
                        }
                        break;
                        
                    case "/api/status":
                        if (request.HttpMethod == "GET")
                        {
                            await HandleStatusRequest(context);
                        }
                        else
                        {
                            await SendJsonResponse(response, 405, new { error = "Method not allowed" });
                        }
                        break;
                        
                    case "/api/clear":
                        if (request.HttpMethod == "POST")
                        {
                            await HandleClearRequest(context);
                        }
                        else
                        {
                            await SendJsonResponse(response, 405, new { error = "Method not allowed" });
                        }
                        break;
                        
                    default:
                        await SendJsonResponse(response, 404, new { error = "Endpoint not found" });
                        break;
                }
            }
            catch (Exception ex)
            {
                await SendJsonResponse(response, 500, new { error = ex.Message });
            }
        }

        private async Task HandleChatRequest(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            try
            {
                using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
                var requestBody = await reader.ReadToEndAsync();
                
                var chatRequest = JsonSerializer.Deserialize<ChatRequest>(requestBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (string.IsNullOrWhiteSpace(chatRequest?.Message))
                {
                    await SendJsonResponse(response, 400, new { error = "Message is required" });
                    return;
                }

                var agent = GetOrCreateAgent();
                var agentResponse = await agent.Execute<Message>(chatRequest.Message);
                
                var responseData = new ChatResponse
                {
                    Response = agentResponse.Content?.ToString() ?? "No response",
                    ToolCalls = agentResponse.ToolCalls != null ? agentResponse.ToolCalls.Count : 0,
                    Timestamp = DateTime.UtcNow
                };

                await SendJsonResponse(response, 200, responseData);
            }
            catch (JsonException)
            {
                await SendJsonResponse(response, 400, new { error = "Invalid JSON in request body" });
            }
            catch (Exception ex)
            {
                await SendJsonResponse(response, 500, new { error = $"Chat processing error: {ex.Message}" });
            }
        }

        private async Task HandleStatusRequest(HttpListenerContext context)
        {
            var response = context.Response;
            
            var statusData = new
            {
                agent = _sharedAgent != null ? "active" : "inactive",
                model = _sharedAgent?.Configuration?.Model ?? "openai/gpt-4.1",
                historyCount = _sharedAgent?.ChatHistory?.Count ?? 0,
                timestamp = DateTime.UtcNow
            };

            await SendJsonResponse(response, 200, statusData);
        }

        private async Task HandleClearRequest(HttpListenerContext context)
        {
            var response = context.Response;
            
            lock (_agentLock)
            {
                _sharedAgent?.ClearHistory();
            }

            await SendJsonResponse(response, 200, new { success = true, message = "Chat history cleared" });
        }

        private DefaultAgent GetOrCreateAgent()
        {
            lock (_agentLock)
            {
                if (_sharedAgent == null)
                {
                    var apiKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
                    var client = new OpenRouterSharp.OpenRouterClient(apiKey);

                    var agentConfig = new AgentConfiguration
                    {
                        Name = "Web Assistant",
                        SystemPrompt = "You are a software engineer, your goal is to help the user write, debug and evaluate code.",
                        Client = client,
                        Model = "openai/gpt-4.1",
                        Temperature = 0.15,
                        MaxTokens = 500,
                        TopP = 0.25,
                        MaintainHistory = true,
                        MaxHistoryMessages = 10,
                        EnableTools = true,
                        ToolNames = new List<string>() { "apply_diff", "grep", "glob", "read_file", "list_files" }
                    };

                    _sharedAgent = new DefaultAgent(agentConfig);
                }

                return _sharedAgent;
            }
        }

        private async Task SendJsonResponse(HttpListenerResponse response, int statusCode, object data)
        {
            response.StatusCode = statusCode;
            response.ContentType = "application/json";
            
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            });
            
            var buffer = Encoding.UTF8.GetBytes(json);
            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        }

        private class ChatRequest
        {
            public string Message { get; set; }
        }

        private class ChatResponse
        {
            public string Response { get; set; }
            public int ToolCalls { get; set; }
            public DateTime Timestamp { get; set; }
        }
    }
}