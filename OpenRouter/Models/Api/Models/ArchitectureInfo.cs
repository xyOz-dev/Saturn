using System.Text.Json.Serialization;

namespace Saturn.OpenRouter.Models.Api.Models
{
    /// <summary>
    /// Architecture details for a model (input/output modalities, tokenizer, instruct type).
    /// </summary>
    public sealed class ArchitectureInfo
    {
        /// <summary>Supported input modalities (e.g., text, image).</summary>
        [JsonPropertyName("input_modalities")]
        public string[]? InputModalities { get; set; }

        /// <summary>Supported output modalities (e.g., text, image).</summary>
        [JsonPropertyName("output_modalities")]
        public string[]? OutputModalities { get; set; }

        /// <summary>Tokenizer identifier if applicable.</summary>
        [JsonPropertyName("tokenizer")]
        public string? Tokenizer { get; set; }

        /// <summary>Instruct type if specified (e.g., chat, completion).</summary>
        [JsonPropertyName("instruct_type")]
        public string? InstructType { get; set; }
    }
}