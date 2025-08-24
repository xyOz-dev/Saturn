using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Saturn.Providers.Models;

namespace Saturn.Providers.ClaudeCLI
{
    public class ClaudeCLIClient : ILLMClient
    {
        public string ProviderName => "Claude CLI";
        public bool IsReady => true;
        
        public async Task<ChatResponse> ChatAsync(ChatRequest request, CancellationToken cancellationToken = default)
        {
            // For non-streaming, we'll use claude --print --output-format json
            var prompt = BuildPrompt(request);
            var result = await RunClaudeCommand(prompt, false, cancellationToken);
            
            return ParseResponse(result);
        }
        
        public async Task<ChatResponse> StreamChatAsync(
            ChatRequest request, 
            Func<StreamChunk, Task> onChunk, 
            CancellationToken cancellationToken = default)
        {
            // For streaming, we could use stream-json format
            // For now, let's fall back to non-streaming
            var response = await ChatAsync(request, cancellationToken);
            
            // Simulate streaming by sending the whole response at once
            await onChunk(new StreamChunk
            {
                Content = response.Message?.Content,
                IsComplete = true
            });
            
            return response;
        }
        
        private string BuildPrompt(ChatRequest request)
        {
            var messages = new StringBuilder();
            
            // Extract system message if present
            var systemMessage = request.Messages?.FirstOrDefault(m => m.Role == "system");
            
            // Get the last user message (Claude CLI expects a single prompt)
            var userMessage = request.Messages?.LastOrDefault(m => m.Role == "user");
            
            if (userMessage != null)
            {
                // If there's conversation history, we need to format it
                if (request.Messages.Count > 1)
                {
                    messages.AppendLine("Previous conversation:");
                    foreach (var msg in request.Messages.Take(request.Messages.Count - 1))
                    {
                        if (msg.Role != "system")
                        {
                            messages.AppendLine($"{msg.Role}: {msg.Content}");
                        }
                    }
                    messages.AppendLine();
                    messages.AppendLine("Current request:");
                }
                
                messages.Append(userMessage.Content);
            }
            
            return messages.ToString();
        }
        
        private async Task<string> RunClaudeCommand(string prompt, bool streaming, CancellationToken cancellationToken)
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = "claude",
                Arguments = "--print --output-format json",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardInputEncoding = Encoding.UTF8,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            
            using var process = new Process { StartInfo = processStartInfo };
            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();
            
            process.OutputDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                {
                    outputBuilder.AppendLine(e.Data);
                }
            };
            
            process.ErrorDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                {
                    errorBuilder.AppendLine(e.Data);
                }
            };
            
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            
            // Write the prompt to stdin
            await process.StandardInput.WriteLineAsync(prompt);
            process.StandardInput.Close();
            
            // Wait for completion or cancellation
            var tcs = new TaskCompletionSource<bool>();
            cancellationToken.Register(() => tcs.TrySetCanceled());
            
            _ = Task.Run(async () =>
            {
                await process.WaitForExitAsync(cancellationToken);
                tcs.TrySetResult(true);
            });
            
            try
            {
                await tcs.Task;
            }
            catch (OperationCanceledException)
            {
                try
                {
                    process.Kill();
                }
                catch { }
                throw;
            }
            
            if (process.ExitCode != 0)
            {
                var error = errorBuilder.ToString();
                throw new Exception($"Claude CLI failed with exit code {process.ExitCode}: {error}");
            }
            
            return outputBuilder.ToString();
        }
        
        private ChatResponse ParseResponse(string jsonOutput)
        {
            try
            {
                using var doc = JsonDocument.Parse(jsonOutput);
                var root = doc.RootElement;
                
                // Parse the Claude CLI JSON response
                var result = root.GetProperty("result").GetString() ?? "";
                var sessionId = root.TryGetProperty("session_id", out var sid) ? sid.GetString() : null;
                
                // Get usage information if available
                ChatUsage usage = null;
                if (root.TryGetProperty("usage", out var usageElement))
                {
                    usage = new ChatUsage
                    {
                        InputTokens = usageElement.TryGetProperty("input_tokens", out var it) ? it.GetInt32() : 0,
                        OutputTokens = usageElement.TryGetProperty("output_tokens", out var ot) ? ot.GetInt32() : 0,
                        TotalTokens = 0 // Will be calculated
                    };
                    usage.TotalTokens = usage.InputTokens + usage.OutputTokens;
                }
                
                return new ChatResponse
                {
                    Id = sessionId ?? Guid.NewGuid().ToString(),
                    Model = "claude", // CLI doesn't tell us the exact model
                    Message = new ChatMessage
                    {
                        Role = "assistant",
                        Content = result
                    },
                    Usage = usage,
                    FinishReason = "stop"
                };
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to parse Claude CLI response: {ex.Message}\nOutput: {jsonOutput}", ex);
            }
        }
        
        public void Dispose()
        {
            // Nothing to dispose
        }
    }
}