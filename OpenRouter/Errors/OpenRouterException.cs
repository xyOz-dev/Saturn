using System.Net;
using System.Text.Json;

namespace Saturn.OpenRouter.Errors
{
    /// <summary>
    /// Exception representing an error response from the OpenRouter API.
    /// Carries HTTP status code, API error code (if provided), and optional metadata.
    /// </summary>
    public sealed class OpenRouterException : Exception
    {
        /// <summary>
        /// The HTTP status code returned by the API.
        /// </summary>
        public HttpStatusCode StatusCode { get; }

        /// <summary>
        /// The API-specific numeric error code, when provided.
        /// </summary>
        public int? ApiErrorCode { get; }

        /// <summary>
        /// Optional metadata payload returned by the API error response.
        /// </summary>
        public IReadOnlyDictionary<string, JsonElement>? Metadata { get; }

        /// <summary>
        /// Initializes a new instance of <see cref="OpenRouterException"/>.
        /// </summary>
        public OpenRouterException(HttpStatusCode statusCode, string message, int? apiErrorCode = null, IReadOnlyDictionary<string, JsonElement>? metadata = null)
            : base(message)
        {
            StatusCode = statusCode;
            ApiErrorCode = apiErrorCode;
            Metadata = metadata;
        }

        /// <summary>
        /// Initializes a new instance of <see cref="OpenRouterException"/> with an inner exception.
        /// </summary>
        public OpenRouterException(HttpStatusCode statusCode, string message, Exception innerException, int? apiErrorCode = null, IReadOnlyDictionary<string, JsonElement>? metadata = null)
            : base(message, innerException)
        {
            StatusCode = statusCode;
            ApiErrorCode = apiErrorCode;
            Metadata = metadata;
        }

        /// <summary>
        /// Creates a readable message including status code and error code for logging or diagnostics.
        /// </summary>
        public override string ToString()
        {
            var codePart = ApiErrorCode.HasValue ? $" (code {ApiErrorCode.Value})" : string.Empty;
            return $"OpenRouter API Error{codePart} [{(int)StatusCode} {StatusCode}]: {Message}";
        }
    }
}