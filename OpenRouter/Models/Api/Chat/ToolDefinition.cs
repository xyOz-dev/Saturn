using System.Text.Json.Serialization;

namespace Saturn.OpenRouter.Models.Api.Chat
{
    public sealed class ToolDefinition
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "function";

        [JsonPropertyName("function")]
        public ToolFunction? Function { get; set; }
    }
}
