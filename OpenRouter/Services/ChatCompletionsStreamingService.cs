using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Saturn.OpenRouter;
using Saturn.OpenRouter.Http;
using Saturn.OpenRouter.Models.Api.Chat;
using Saturn.OpenRouter.Serialization;

namespace Saturn.OpenRouter.Services
{
    /// <summary>
    /// Streaming Chat Completions service using Server-Sent Events (SSE).
    /// </summary>
    public sealed class ChatCompletionsStreamingService
    {
        private readonly HttpClientAdapter _http;
        private readonly OpenRouterOptions _options;
        private readonly JsonSerializerOptions _jsonOptions;

        /// <summary>
        /// Internal constructor. Instances are exposed via OpenRouterClient.
        /// </summary>
        /// <param name="http">HTTP adapter with configured base URL, authorization, and headers.</param>
        /// <param name="options">Client options providing JSON settings and defaults.</param>
        internal ChatCompletionsStreamingService(HttpClientAdapter http, OpenRouterOptions options)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _jsonOptions = _options.CreateJsonOptions();
        }

        /// <summary>
        /// POST /chat/completions (streaming via SSE).
        /// Ensures stream=true and yields typed ChatCompletionChunk events as they arrive.
        /// Comment lines and non-JSON payloads (including "[DONE]") are ignored safely.
        /// </summary>
        /// <param name="request">Chat completion request. Stream flag will be forced to true if not already.</param>
        /// <param name="cancellationToken">Cancellation token to stop the stream promptly.</param>
        /// <returns>Async sequence of ChatCompletionChunk values.</returns>
        public async IAsyncEnumerable<ChatCompletionChunk> StreamAsync(
            ChatCompletionRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (request is null) throw new ArgumentNullException(nameof(request));

            // Ensure streaming
            if (request.Stream != true)
            {
                request.Stream = true;
            }

            var json = Json.Serialize(request, _jsonOptions);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            await foreach (var ev in _http.StartSseAsync(
                HttpMethod.Post,
                "chat/completions",
                content,
                headers: null,
                cancellationToken: cancellationToken).ConfigureAwait(false))
            {
                if (cancellationToken.IsCancellationRequested)
                    yield break;

                if (ev.IsComment)
                    continue;

                var data = ev.Data;
                if (string.IsNullOrWhiteSpace(data))
                    continue;

                if (data == "[DONE]")
                    yield break; // stop gracefully

                ChatCompletionChunk? parsed;
                try
                {
                    parsed = Json.Deserialize<ChatCompletionChunk>(data, _jsonOptions);
                }
                catch (JsonException)
                {
                    // Skip non-JSON payloads (e.g., pings)
                    continue;
                }
                catch
                {
                    // Defensive: skip unknown payloads
                    continue;
                }

                if (parsed != null)
                {
                    yield return parsed;
                }
            }
        }
    }
}