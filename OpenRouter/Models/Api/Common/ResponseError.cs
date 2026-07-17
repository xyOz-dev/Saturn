using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Saturn.OpenRouter.Models.Api.Common
{
    public sealed class ResponseError
    {
        [JsonPropertyName("code")]
        [JsonConverter(typeof(Serialization.LenientNullableIntConverter))]
        public int? Code { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("metadata")]
        public Dictionary<string, JsonElement>? Metadata { get; set; }
    }
}
