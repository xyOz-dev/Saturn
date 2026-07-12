using System.Text.Json;
using System.Text.Json.Serialization;

namespace Saturn.OpenRouter.Serialization
{
    public static class Json
    {
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

        public static string Serialize<T>(T value, JsonSerializerOptions? options = null)
            => System.Text.Json.JsonSerializer.Serialize(value, options ?? CreateDefaultOptions());

        public static async Task<T?> DeserializeAsync<T>(Stream utf8Json, JsonSerializerOptions? options = null, CancellationToken cancellationToken = default)
            => await System.Text.Json.JsonSerializer.DeserializeAsync<T>(utf8Json, options ?? CreateDefaultOptions(), cancellationToken).ConfigureAwait(false);

        public static T? Deserialize<T>(string json, JsonSerializerOptions? options = null)
            => System.Text.Json.JsonSerializer.Deserialize<T>(json, options ?? CreateDefaultOptions());
    }
}