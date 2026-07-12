using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;

namespace Saturn.OpenRouter.Headers
{
    public static class AppAttributionHeaders
    {
        public const string HttpRefererHeader = "HTTP-Referer";

        public const string XTitleHeader = "X-Title";

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