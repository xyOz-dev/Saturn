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
                cancellationToken.ThrowIfCancellationRequested();

                if (ev.IsComment)
                    continue;

                var data = ev.Data;
                if (string.IsNullOrWhiteSpace(data))
                    continue;

                if (data == "[DONE]")
                    yield break;

                var streamError = TryParseStreamError(data);
                if (streamError != null)
                    throw streamError;

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

        private Errors.OpenRouterException? TryParseStreamError(string data)
        {
            // Fast path: the vast majority of SSE payloads are ordinary content
            // chunks with no "error" field, so skip the extra deserialization for them.
            if (data.IndexOf("error", StringComparison.Ordinal) < 0)
                return null;

            try
            {
                var parsed = Json.Deserialize<Models.Api.ErrorResponse>(data, _jsonOptions);
                if (parsed?.Error == null || (parsed.Error.Code == null && string.IsNullOrWhiteSpace(parsed.Error.Message)))
                {
                    return null;
                }

                var status = parsed.Error.Code is >= 400 and <= 599
                    ? (System.Net.HttpStatusCode)parsed.Error.Code.Value
                    : System.Net.HttpStatusCode.BadGateway;

                return Errors.OpenRouterException.FromErrorBody(status, parsed.Error);
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
