using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Saturn.OpenRouter;
using Saturn.OpenRouter.Http;
using Saturn.OpenRouter.Models.Api.Chat;
using Saturn.OpenRouter.Models.Api.Models;
using Saturn.OpenRouter.Services;

namespace Saturn.Providers
{
    /// <summary>
    /// Client for LM Studio's local server. Chat goes through the OpenAI-compatible
    /// /v1 endpoints, reusing the SDK's HTTP/SSE plumbing with a different base URL and
    /// no auth. Model listings are enriched best-effort from LM Studio's native REST API
    /// (/api/v0/models), which knows context lengths and which models are loaded.
    /// </summary>
    public sealed class LMStudioClient : ILlmClient
    {
        private readonly HttpClientAdapter _v1Http;
        private readonly HttpClientAdapter _nativeHttp;
        private readonly ChatCompletionsService _chat;
        private readonly ChatCompletionsStreamingService _chatStreaming;
        private readonly string _baseUrl;

        public LMStudioClient(string baseUrl, TimeSpan timeout, Func<HttpMessageHandler>? handlerFactory = null)
        {
            var root = NormalizeRootUrl(baseUrl);
            _baseUrl = root + "/v1";

            // ApiKey is deliberately left null: LM Studio ignores auth, and constructing
            // the adapter directly (instead of OpenRouterClient) avoids picking up
            // OPENROUTER_API_KEY from the environment and sending it to a local server.
            var v1Options = new OpenRouterOptions
            {
                BaseUrl = _baseUrl,
                Timeout = timeout,
                HttpMessageHandler = handlerFactory?.Invoke()
            };
            _v1Http = new HttpClientAdapter(v1Options);
            try
            {
                _chat = new ChatCompletionsService(_v1Http);
                _chatStreaming = new ChatCompletionsStreamingService(_v1Http, v1Options);

                _nativeHttp = new HttpClientAdapter(new OpenRouterOptions
                {
                    BaseUrl = root + "/api/v0",
                    Timeout = TimeSpan.FromSeconds(10),
                    HttpMessageHandler = handlerFactory?.Invoke()
                });
            }
            catch
            {
                _v1Http.Dispose();
                throw;
            }
        }

        public string BaseUrl => _baseUrl;

        public LlmClientCapabilities Capabilities { get; } = new()
        {
            ProviderName = "LM Studio",
            RequiresApiKey = false,
            SupportsTransforms = false,
            SupportsUsageInclude = false,
            SupportsToolChoice = true,
            SupportsPricing = false,
            SupportsCaching = false,
            DefaultModel = string.Empty,
            ModelListCacheDuration = TimeSpan.FromSeconds(30),
            FallbackModels = Array.Empty<ModelInfo>()
        };

        internal static string NormalizeRootUrl(string baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                throw new InvalidOperationException("LM Studio base URL is empty.");
            }

            var root = baseUrl.Trim().TrimEnd('/');
            if (root.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            {
                root = root.Substring(0, root.Length - "/v1".Length).TrimEnd('/');
            }

            // Require an explicit http(s) scheme: "localhost:1234" parses as a URI with
            // scheme "localhost" and would only fail later with a generic connect error.
            if (!Uri.TryCreate(root, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new InvalidOperationException(
                    $"LM Studio base URL '{baseUrl}' is not a valid http(s) URL. Example: http://localhost:1234/v1");
            }

            return root;
        }

        public async Task<ChatCompletionResponse?> ChatAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default)
        {
            try
            {
                return await _chat.CreateAsync(request, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                throw Unreachable(ex);
            }
        }

        public async IAsyncEnumerable<ChatCompletionChunk> StreamChatAsync(
            ChatCompletionRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var enumerator = _chatStreaming.StreamAsync(request, cancellationToken).GetAsyncEnumerator(cancellationToken);
            await using (enumerator.ConfigureAwait(false))
            {
                while (true)
                {
                    bool moved;
                    try
                    {
                        moved = await enumerator.MoveNextAsync();
                    }
                    catch (HttpRequestException ex)
                    {
                        throw Unreachable(ex);
                    }

                    if (!moved)
                    {
                        yield break;
                    }

                    yield return enumerator.Current;
                }
            }
        }

        public async Task<List<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
        {
            ModelListResponse? response;
            try
            {
                response = await _v1Http.SendJsonAsync<ModelListResponse>(HttpMethod.Get, "models", null, null, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                throw Unreachable(ex);
            }

            var models = (response?.Data ?? Array.Empty<Model>())
                .Where(m => !string.IsNullOrEmpty(m.Id))
                .Select(m => new ModelInfo { Id = m.Id! })
                .ToList();

            await EnrichFromNativeApiAsync(models, cancellationToken);

            return models
                .OrderByDescending(m => m.IsLoaded == true)
                .ThenBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Fills in context length and loaded state from /api/v0/models. That API is
        /// beta and may be disabled or change shape, so any failure leaves the baseline
        /// /v1 listing untouched.
        /// </summary>
        private async Task EnrichFromNativeApiAsync(List<ModelInfo> models, CancellationToken cancellationToken)
        {
            if (models.Count == 0)
            {
                return;
            }

            JsonElement root;
            try
            {
                root = await _nativeHttp.SendJsonAsync<JsonElement>(HttpMethod.Get, "models", null, null, cancellationToken);
            }
            catch
            {
                return;
            }

            try
            {
                if (root.ValueKind != JsonValueKind.Object ||
                    !root.TryGetProperty("data", out var data) ||
                    data.ValueKind != JsonValueKind.Array)
                {
                    return;
                }

                var byId = models.ToDictionary(m => m.Id, StringComparer.OrdinalIgnoreCase);
                foreach (var entry in data.EnumerateArray())
                {
                    if (entry.ValueKind != JsonValueKind.Object ||
                        !entry.TryGetProperty("id", out var idProp) ||
                        idProp.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    if (!byId.TryGetValue(idProp.GetString()!, out var model))
                    {
                        continue;
                    }

                    if (entry.TryGetProperty("max_context_length", out var ctxProp) &&
                        ctxProp.ValueKind == JsonValueKind.Number &&
                        ctxProp.TryGetInt32(out var contextLength) &&
                        contextLength > 0)
                    {
                        model.ContextLength = contextLength;
                    }

                    if (entry.TryGetProperty("state", out var stateProp) &&
                        stateProp.ValueKind == JsonValueKind.String)
                    {
                        model.IsLoaded = string.Equals(stateProp.GetString(), "loaded", StringComparison.OrdinalIgnoreCase);
                    }
                }
            }
            catch
            {
            }
        }

        public async Task<bool> ValidateConnectionAsync(CancellationToken cancellationToken = default)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                await _v1Http.SendJsonAsync<ModelListResponse>(HttpMethod.Get, "models", null, null, timeout.Token);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private InvalidOperationException Unreachable(Exception inner) =>
            new(
                $"Cannot reach LM Studio at {_baseUrl}. Is LM Studio running with its local server started? " +
                "(In LM Studio: Developer tab -> Start Server.)",
                inner);

        public void Dispose()
        {
            try
            {
                _v1Http.Dispose();
            }
            finally
            {
                _nativeHttp.Dispose();
            }
        }
    }
}
