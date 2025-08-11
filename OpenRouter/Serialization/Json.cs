using System.Text.Json;
using System.Text.Json.Serialization;

namespace Saturn.OpenRouter.Serialization
{
    /// <summary>
    /// Centralized JSON helpers and default options for OpenRouter client.
    /// Uses camelCase, ignores nulls, allows trailing commas, and is case-insensitive.
    /// </summary>
    public static class Json
    {
        /// <summary>
        /// Creates default JsonSerializerOptions with common OpenRouter settings.
        /// </summary>
        public static JsonSerializerOptions CreateDefaultOptions(
            bool useCamelCase = true,
            bool ignoreNulls = true,
            bool allowTrailingCommas = true,
            bool caseInsensitive = true)
        {
            var opts = new JsonSerializerOptions
            {
                PropertyNamingPolicy = useCamelCase ? JsonNamingPolicy.CamelCase : null,
                AllowTrailingCommas = allowTrailingCommas,
                PropertyNameCaseInsensitive = caseInsensitive,
                WriteIndented = false
            };

            if (ignoreNulls)
            {
                opts.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            }

            return opts;
        }

        /// <summary>
        /// Serialize a value with provided or default options.
        /// </summary>
        public static string Serialize<T>(T value, JsonSerializerOptions? options = null)
            => System.Text.Json.JsonSerializer.Serialize(value, options ?? CreateDefaultOptions());

        /// <summary>
        /// Deserialize a stream into a value with provided or default options.
        /// </summary>
        public static async Task<T?> DeserializeAsync<T>(Stream utf8Json, JsonSerializerOptions? options = null, CancellationToken cancellationToken = default)
            => await System.Text.Json.JsonSerializer.DeserializeAsync<T>(utf8Json, options ?? CreateDefaultOptions(), cancellationToken).ConfigureAwait(false);

        /// <summary>
        /// Deserialize a JSON string into a value with provided or default options.
        /// </summary>
        public static T? Deserialize<T>(string json, JsonSerializerOptions? options = null)
            => System.Text.Json.JsonSerializer.Deserialize<T>(json, options ?? CreateDefaultOptions());
    }
}