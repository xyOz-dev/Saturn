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
    public sealed class ChatCompletionsStreamingService
    {
        private readonly HttpClientAdapter _http;
        private readonly OpenRouterOptions _options;
        private readonly JsonSerializerOptions _jsonOptions;

        internal ChatCompletionsStreamingService(HttpClientAdapter http, OpenRouterOptions options)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _jsonOptions = _options.CreateJsonOptions();
        }

        public async IAsyncEnumerable<ChatCompletionChunk> StreamAsync(
            ChatCompletionRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (request is null) throw new ArgumentNullException(nameof(request));

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
                    yield break;

                ChatCompletionChunk? parsed;
                try
                {
                    parsed = Json.Deserialize<ChatCompletionChunk>(data, _jsonOptions);
                }
                catch (JsonException)
                {
                    continue;
                }
                catch
                {
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
