using System;

namespace Saturn.Providers
{
    /// <summary>
    /// Provider-neutral model metadata surfaced to the UI. Fields that a provider
    /// cannot supply (pricing on local servers, context length on bare OpenAI-compatible
    /// endpoints) are simply left null and the UI degrades accordingly.
    /// </summary>
    public sealed class ModelInfo
    {
        public string Id { get; set; } = string.Empty;

        /// <summary>Human-friendly name; falls back to Id when the provider has none.</summary>
        public string? DisplayName { get; set; }

        public string? Description { get; set; }

        public int? ContextLength { get; set; }

        /// <summary>Per-token prompt price as provided by the API (string form, provider units).</summary>
        public string? PromptPrice { get; set; }

        /// <summary>Per-token completion price as provided by the API (string form, provider units).</summary>
        public string? CompletionPrice { get; set; }

        /// <summary>Whether the model is currently loaded in memory. Only meaningful for local providers.</summary>
        public bool? IsLoaded { get; set; }

        public string Display => string.IsNullOrWhiteSpace(DisplayName) ? Id : DisplayName!;
    }
}
