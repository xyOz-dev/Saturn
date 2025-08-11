using System.Text.Json;
using System.Text.Json.Serialization;

namespace Saturn.OpenRouter.Models.Api.Common
{
    /// <summary>
    /// Response format directive. Supports "json_object" mode or JSON Schema enforcement via "json_schema".
    /// </summary>
    public sealed class ResponseFormat
    {
        /// <summary>Type of response format: "json_object" | "json_schema".</summary>
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        /// <summary>JSON Schema configuration when type == "json_schema".</summary>
        [JsonPropertyName("json_schema")]
        public JsonSchemaSpec? JsonSchema { get; set; }

        /// <summary>JSON Schema specification embedded in the request.</summary>
        public sealed class JsonSchemaSpec
        {
            /// <summary>Name of the schema.</summary>
            [JsonPropertyName("name")]
            public string? Name { get; set; }

            /// <summary>When true, enforce strict schema adherence.</summary>
            [JsonPropertyName("strict")]
            public bool? Strict { get; set; }

            /// <summary>Arbitrary JSON Schema object.</summary>
            [JsonPropertyName("schema")]
            public JsonElement Schema { get; set; }
        }
    }
}
