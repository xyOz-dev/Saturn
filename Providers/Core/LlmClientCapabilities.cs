using System;
using System.Collections.Generic;

namespace Saturn.Providers
{
    /// <summary>
    /// Static traits of a connected LLM client. Request builders and UI consult these
    /// instead of branching on provider names, so new providers only need to describe
    /// themselves accurately to get correct behavior everywhere.
    /// </summary>
    public sealed class LlmClientCapabilities
    {
        /// <summary>Display name shown in the status bar and dialogs (e.g. "OpenRouter", "LM Studio").</summary>
        public string ProviderName { get; init; } = string.Empty;

        public bool RequiresApiKey { get; init; }

        /// <summary>Supports the OpenRouter "transforms" request extension (e.g. middle-out compression).</summary>
        public bool SupportsTransforms { get; init; }

        /// <summary>Supports the OpenRouter "usage": {"include": true} request extension.</summary>
        public bool SupportsUsageInclude { get; init; }

        /// <summary>Safe to send "tool_choice" alongside a tools array.</summary>
        public bool SupportsToolChoice { get; init; } = true;

        /// <summary>Model listings carry pricing information worth displaying.</summary>
        public bool SupportsPricing { get; init; }

        /// <summary>Supports Anthropic-style cache_control content parts.</summary>
        public bool SupportsCaching { get; init; }

        /// <summary>Model used when nothing is configured or the configured model is unavailable.</summary>
        public string DefaultModel { get; init; } = string.Empty;

        /// <summary>
        /// How long a fetched model list stays fresh. Local providers should keep this short:
        /// their list changes whenever the user loads or unloads a model.
        /// </summary>
        public TimeSpan ModelListCacheDuration { get; init; } = TimeSpan.FromMinutes(30);

        /// <summary>Models offered when the live listing cannot be fetched. Empty for local providers.</summary>
        public IReadOnlyList<ModelInfo> FallbackModels { get; init; } = Array.Empty<ModelInfo>();
    }
}
