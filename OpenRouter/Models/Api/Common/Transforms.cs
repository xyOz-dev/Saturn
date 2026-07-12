using System.Text.Json.Serialization;

namespace Saturn.OpenRouter.Models.Api.Common
{
    public sealed class Transforms
    {
        [JsonPropertyName("transforms")]
        public string[]? Items { get; set; }
    }
}
