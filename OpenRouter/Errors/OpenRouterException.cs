using System.Net;
using System.Text.Json;
using Saturn.OpenRouter.Models.Api;

namespace Saturn.OpenRouter.Errors
{
    public sealed class OpenRouterException : Exception
    {
        public HttpStatusCode StatusCode { get; }

        public int? ApiErrorCode { get; }

        public IReadOnlyDictionary<string, JsonElement>? Metadata { get; }

        public TimeSpan? RetryAfter { get; }

        public OpenRouterException(HttpStatusCode statusCode, string message, int? apiErrorCode = null, IReadOnlyDictionary<string, JsonElement>? metadata = null, TimeSpan? retryAfter = null)
            : base(message)
        {
            StatusCode = statusCode;
            ApiErrorCode = apiErrorCode;
            Metadata = metadata;
            RetryAfter = retryAfter;
        }

        public OpenRouterException(HttpStatusCode statusCode, string message, Exception innerException, int? apiErrorCode = null, IReadOnlyDictionary<string, JsonElement>? metadata = null, TimeSpan? retryAfter = null)
            : base(message, innerException)
        {
            StatusCode = statusCode;
            ApiErrorCode = apiErrorCode;
            Metadata = metadata;
            RetryAfter = retryAfter;
        }

        public static OpenRouterException FromErrorBody(HttpStatusCode statusCode, ErrorResponse.ErrorBody error, string? fallbackMessage = null, TimeSpan? retryAfter = null)
        {
            var message = error.Message ?? fallbackMessage ?? $"Request failed with status {(int)statusCode} {statusCode}.";
            var metadata = error.Metadata;

            if (metadata != null && metadata.Count > 0)
            {
                try
                {
                    if (metadata.TryGetValue("provider_name", out var providerNameEl) && providerNameEl.ValueKind == JsonValueKind.String)
                    {
                        var providerName = providerNameEl.GetString();
                        if (!string.IsNullOrWhiteSpace(providerName))
                        {
                            message += $" | provider={providerName}";
                        }
                    }
                    if (metadata.TryGetValue("raw", out var rawEl))
                    {
                        var rawStr = rawEl.ToString();
                        if (!string.IsNullOrWhiteSpace(rawStr))
                        {
                            var snippet = Truncate(rawStr, 512).Replace("\r", " ").Replace("\n", " ");
                            message += $" | raw={snippet}";
                        }
                    }
                }
                catch
                {

                }
            }

            return new OpenRouterException(statusCode, message, apiErrorCode: error.Code, metadata: metadata, retryAfter: retryAfter);
        }

        private static string Truncate(string value, int max)
            => value.Length <= max ? value : value.Substring(0, max);

        public override string ToString()
        {
            var codePart = ApiErrorCode.HasValue ? $" (code {ApiErrorCode.Value})" : string.Empty;
            return $"OpenRouter API Error{codePart} [{(int)StatusCode} {StatusCode}]: {Message}";
        }
    }
}