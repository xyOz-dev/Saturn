using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Saturn.OpenRouter.Errors;
using Saturn.OpenRouter.Headers;
using Saturn.OpenRouter.Models.Api;
using Saturn.OpenRouter.Serialization;

namespace Saturn.OpenRouter.Http
{
    public sealed class HttpClientAdapter : IDisposable
    {
        private readonly HttpClient _http;
        private readonly OpenRouterOptions _options;

        public HttpClientAdapter(OpenRouterOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            var handler = options.HttpMessageHandler ?? new HttpClientHandler();
            _http = new HttpClient(handler)
            {
                Timeout = options.Timeout
            };

            var baseUrl = string.IsNullOrWhiteSpace(options.BaseUrl)
                ? "https://openrouter.ai/api/v1"
                : options.BaseUrl;

            if (!baseUrl.EndsWith("/"))
            {
                baseUrl += "/";
            }

            _http.BaseAddress = new Uri(baseUrl, UriKind.Absolute);
        }

        public HttpRequestMessage CreateRequest(HttpMethod method, string path, IDictionary<string, string>? headers = null, bool acceptSse = false)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));

            var request = new HttpRequestMessage(method, path);

            if (!string.IsNullOrWhiteSpace(_options.ApiKey))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
            }

            foreach (var kvp in _options.DefaultHeaders)
            {
                if (!request.Headers.Contains(kvp.Key))
                {
                    request.Headers.TryAddWithoutValidation(kvp.Key, kvp.Value);
                }
            }

            AppAttributionHeaders.Append(request, _options, headers);

            if (headers != null)
            {
                foreach (var kvp in headers)
                {
                    if (string.Equals(kvp.Key, "Content-Type", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (request.Headers.Contains(kvp.Key))
                        request.Headers.Remove(kvp.Key);

                    request.Headers.TryAddWithoutValidation(kvp.Key, kvp.Value);
                }
            }

            var acceptValue = acceptSse ? "text/event-stream" : "application/json";
            if (!request.Headers.Accept.Any())
            {
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(acceptValue));
            }

            return request;
        }

        public async Task<TResponse?> SendJsonAsync<TResponse>(
            HttpMethod method,
            string path,
            object? body = null,
            IDictionary<string, string>? headers = null,
            CancellationToken cancellationToken = default)
        {
            var request = CreateRequest(method, path, headers, acceptSse: false);

            if (body != null)
            {
                var json = Json.Serialize(body, _options.CreateJsonOptions());
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                await ThrowForErrorAsync(response, cancellationToken).ConfigureAwait(false);
                return default;
            }

            if (response.Content is null)
                return default;

            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            if (stream == Stream.Null)
                return default;
            var resp = await Json.DeserializeAsync<TResponse>(stream, _options.CreateJsonOptions(), cancellationToken).ConfigureAwait(false);
            return resp;
        }

        public async Task<HttpResponseMessage> SendRawAsync(
            HttpMethod method,
            string path,
            HttpContent? content = null,
            IDictionary<string, string>? headers = null,
            CancellationToken cancellationToken = default)
        {
            var request = CreateRequest(method, path, headers, acceptSse: false);
            request.Content = content;

            var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                try
                {
                    await ThrowForErrorAsync(response, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    response.Dispose();
                }
            }

            return response;
        }

        public async IAsyncEnumerable<SseEvent> StartSseAsync(
            HttpMethod method,
            string path,
            HttpContent? content = null,
            IDictionary<string, string>? headers = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var request = CreateRequest(method, path, headers, acceptSse: true);
            request.Headers.Accept.Clear();
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

            if (content != null)
            {
                if (content.Headers.ContentType is null)
                {
                    content.Headers.ContentType = new MediaTypeHeaderValue("application/json")
                    {
                        CharSet = "utf-8"
                    };
                }
                request.Content = content;
            }

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                await ThrowForErrorAsync(response, cancellationToken).ConfigureAwait(false);
                yield break;
            }

            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

            await foreach (var evt in SseStream.ReadEventsAsync(stream, cancellationToken).ConfigureAwait(false))
            {
                yield return evt;
            }
        }

        private async Task ThrowForErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            var status = response.StatusCode;
            var retryAfter = GetRetryAfter(response);
            string? payload = null;
            try
            {
                using var readCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                readCts.CancelAfter(TimeSpan.FromSeconds(30));
                payload = response.Content is null
                    ? null
                    : await response.Content.ReadAsStringAsync(readCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {

            }

            try
            {
                if (!string.IsNullOrWhiteSpace(payload))
                {
                    var parsed = System.Text.Json.JsonSerializer.Deserialize<ErrorResponse>(payload!, _options.CreateJsonOptions());
                    if (parsed?.Error != null)
                    {
                        throw OpenRouterException.FromErrorBody(
                            status,
                            parsed.Error,
                            fallbackMessage: $"Request failed with status {(int)status} {response.ReasonPhrase}.",
                            retryAfter: retryAfter);
                    }
                }
            }
            catch (OpenRouterException)
            {
                throw;
            }
            catch
            {

            }

            var generic = $"Request failed with status {(int)status} {response.ReasonPhrase}.";
            if (!string.IsNullOrWhiteSpace(payload))
            {
                generic += $" Body: {Truncate(payload!, 2048)}";
            }

            throw new OpenRouterException(status, generic, retryAfter: retryAfter);
        }

        private static TimeSpan? GetRetryAfter(HttpResponseMessage response)
        {
            var retryAfter = response.Headers.RetryAfter;
            if (retryAfter == null) return null;

            if (retryAfter.Delta.HasValue)
            {
                return retryAfter.Delta.Value > TimeSpan.Zero ? retryAfter.Delta : null;
            }

            if (retryAfter.Date.HasValue)
            {
                var delta = retryAfter.Date.Value - DateTimeOffset.UtcNow;
                return delta > TimeSpan.Zero ? delta : null;
            }

            return null;
        }

        private static string Truncate(string value, int max)
            => value.Length <= max ? value : value.Substring(0, max);

        public void Dispose()
        {
            _http.Dispose();
        }
    }
}