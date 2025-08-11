using System.Net.Http;
using System.Text.Json;

using Saturn.OpenRouter.Serialization;

namespace Saturn.OpenRouter
{
    /// <summary>
    /// Global configuration for the OpenRouter client, including base URL, API key, timeouts,
    /// default headers (such as HTTP-Referer and X-Title), optional HttpMessageHandler injection,
    /// and JSON options toggles.
    /// </summary>
    public sealed class OpenRouterOptions
    {
        /// <summary>
        /// Base API URL. Defaults to https://openrouter.ai/api/v1
        /// </summary>
        public string BaseUrl { get; set; } = "https://openrouter.ai/api/v1";

        /// <summary>
        /// API key for authenticated endpoints. If not provided, the client will attempt to read
        /// from environment variable OPENROUTER_API_KEY at construction time.
        /// </summary>
        public string? ApiKey { get; set; }

        /// <summary>
        /// Default request timeout for the underlying HttpClient.
        /// </summary>
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(100);

        /// <summary>
        /// Default headers applied to every request if not explicitly overridden per call.
        /// Keys are compared case-insensitively.
        /// </summary>
        public IDictionary<string, string> DefaultHeaders { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Application attribution header value for HTTP-Referer.
        /// When set, it will be added as the HTTP-Referer header if not explicitly provided per request.
        /// </summary>
        public string? Referer { get; set; }

        /// <summary>
        /// Application attribution header value for X-Title.
        /// When set, it will be added as the X-Title header if not explicitly provided per request.
        /// </summary>
        public string? Title { get; set; }

        /// <summary>
        /// Optional HttpMessageHandler used to construct the underlying HttpClient, allowing for
        /// testing/mocking or custom handlers (e.g., for in-memory tests).
        /// </summary>
        public HttpMessageHandler? HttpMessageHandler { get; set; }

        // JSON options toggles

        /// <summary>
        /// Use camelCase property naming during serialization.
        /// </summary>
        public bool UseCamelCase { get; set; } = true;

        /// <summary>
        /// Ignore null values when writing JSON.
        /// </summary>
        public bool IgnoreNulls { get; set; } = true;

        /// <summary>
        /// Allow trailing commas when reading JSON.
        /// </summary>
        public bool AllowTrailingCommas { get; set; } = true;

        /// <summary>
        /// Treat property names case-insensitively when reading JSON.
        /// </summary>
        public bool CaseInsensitiveProps { get; set; } = true;

        /// <summary>
        /// Create JsonSerializerOptions instance based on the toggles in this options object.
        /// </summary>
        public JsonSerializerOptions CreateJsonOptions() =>
            Json.CreateDefaultOptions(
                useCamelCase: UseCamelCase,
                ignoreNulls: IgnoreNulls,
                allowTrailingCommas: AllowTrailingCommas,
                caseInsensitive: CaseInsensitiveProps
            );
    }
}