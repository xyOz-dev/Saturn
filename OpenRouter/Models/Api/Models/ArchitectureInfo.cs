using System.Text.Json.Serialization;

namespace Saturn.OpenRouter.Models.Api.Models
{
    public sealed class ArchitectureInfo
    {
        [JsonPropertyName("input_modalities")]
        public string[]? InputModalities { get; set; }

        [JsonPropertyName("output_modalities")]
        public string[]? OutputModalities { get; set; }

        [JsonPropertyName("tokenizer")]
        public string? Tokenizer { get; set; }

        [JsonPropertyName("instruct_type")]
        public string? InstructType { get; set; }
    }
}