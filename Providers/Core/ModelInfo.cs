using System;

namespace Saturn.Providers
{
    public sealed class ModelInfo
    {
        public string Id { get; set; } = string.Empty;

        public string? DisplayName { get; set; }

        public string? Description { get; set; }

        public int? ContextLength { get; set; }

        public int? MaxCompletionTokens { get; set; }

        public string? PromptPrice { get; set; }

        public string? CompletionPrice { get; set; }

        public bool? IsLoaded { get; set; }

        public string Display => string.IsNullOrWhiteSpace(DisplayName) ? Id : DisplayName!;
    }
}
