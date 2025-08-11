using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;

namespace Saturn.OpenRouter.Headers
{
    /// <summary>
    /// Helper to append application attribution headers according to OpenRouter requirements.
    /// Builds "HTTP-Referer" and "X-Title" headers, merging per-call extras without overwriting
    /// explicit request headers.
    /// </summary>
    public static class AppAttributionHeaders
    {
        /// <summary>Header name carrying the application referer.</summary>
        public const string HttpRefererHeader = "HTTP-Referer";

        /// <summary>Header name carrying the application title.</summary>
        public const string XTitleHeader = "X-Title";

        /// <summary>
        /// Append attribution headers to the request based on options, unless already present.
        /// Values provided via per-call headers take precedence over options.
        /// </summary>
        /// <param name="request">The outgoing HTTP request.</param>
        /// <param name="options">OpenRouter client options containing attribution values.</param>
        /// <param name="perCallHeaders">Optional per-call header overrides.</param>
        public static void Append(HttpRequestMessage request, OpenRouterOptions options, IDictionary<string, string>? perCallHeaders = null)
        {
            if (request is null) throw new ArgumentNullException(nameof(request));
            if (options is null) throw new ArgumentNullException(nameof(options));

            bool PerCallHas(string name) => perCallHeaders != null && perCallHeaders.Keys.Any(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase));
            bool RequestHas(string name) => request.Headers.Contains(name);

            if (!RequestHas(HttpRefererHeader) && !PerCallHas(HttpRefererHeader) && !string.IsNullOrWhiteSpace(options.Referer))
            {
                request.Headers.TryAddWithoutValidation(HttpRefererHeader, options.Referer!);
            }

            if (!RequestHas(XTitleHeader) && !PerCallHas(XTitleHeader) && !string.IsNullOrWhiteSpace(options.Title))
            {
                request.Headers.TryAddWithoutValidation(XTitleHeader, options.Title!);
            }
        }
    }
}