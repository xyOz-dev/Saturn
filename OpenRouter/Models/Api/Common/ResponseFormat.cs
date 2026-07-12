using System.Text.Json;
using System.Text.Json.Serialization;

namespace Saturn.OpenRouter.Models.Api.Common
{
    public sealed class ResponseFormat
    {
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("json_schema")]
        public JsonSchemaSpec? JsonSchema { get; set; }

        public sealed class JsonSchemaSpec
        {
            [JsonPropertyName("name")]
            public string? Name { get; set; }

            [JsonPropertyName("strict")]
            public bool? Strict { get; set; }

            [JsonPropertyName("schema")]
            public JsonElement Schema { get; set; }
        }
    }
}
