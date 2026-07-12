using System.Net;
using System.Text.Json;

namespace Saturn.OpenRouter.Errors
{
    public sealed class OpenRouterException : Exception
    {
        public HttpStatusCode StatusCode { get; }

        public int? ApiErrorCode { get; }

        public IReadOnlyDictionary<string, JsonElement>? Metadata { get; }

        public OpenRouterException(HttpStatusCode statusCode, string message, int? apiErrorCode = null, IReadOnlyDictionary<string, JsonElement>? metadata = null)
            : base(message)
        {
            StatusCode = statusCode;
            ApiErrorCode = apiErrorCode;
            Metadata = metadata;
        }

        public OpenRouterException(HttpStatusCode statusCode, string message, Exception innerException, int? apiErrorCode = null, IReadOnlyDictionary<string, JsonElement>? metadata = null)
            : base(message, innerException)
        {
            StatusCode = statusCode;
            ApiErrorCode = apiErrorCode;
            Metadata = metadata;
        }

        public override string ToString()
        {
            var codePart = ApiErrorCode.HasValue ? $" (code {ApiErrorCode.Value})" : string.Empty;
            return $"OpenRouter API Error{codePart} [{(int)StatusCode} {StatusCode}]: {Message}";
        }
    }
}