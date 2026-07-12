using System.Net.Http;
using System.Text.Json;

using Saturn.OpenRouter.Serialization;

namespace Saturn.OpenRouter
{
    public sealed class OpenRouterOptions
    {
        public string BaseUrl { get; set; } = "https://openrouter.ai/api/v1";

        public string? ApiKey { get; set; }

        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(100);

        public IDictionary<string, string> DefaultHeaders { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public string? Referer { get; set; }

        public string? Title { get; set; }

        public HttpMessageHandler? HttpMessageHandler { get; set; }

        public bool UseCamelCase { get; set; } = true;

        public bool IgnoreNulls { get; set; } = true;

        public bool AllowTrailingCommas { get; set; } = true;

        public bool CaseInsensitiveProps { get; set; } = true;

        public JsonSerializerOptions CreateJsonOptions() =>
            Json.CreateDefaultOptions(
                useCamelCase: UseCamelCase,
                ignoreNulls: IgnoreNulls,
                allowTrailingCommas: AllowTrailingCommas,
                caseInsensitive: CaseInsensitiveProps
            );
    }
}