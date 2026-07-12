using System.Text.Json.Serialization;

namespace Saturn.OpenRouter.Models.Api.Common
{
    public sealed class UsageOption
    {
        [JsonPropertyName("include")]
        public bool? Include { get; set; }
    }
}
