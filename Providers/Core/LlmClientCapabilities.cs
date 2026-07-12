using System;
using System.Collections.Generic;

namespace Saturn.Providers
{
    public sealed class LlmClientCapabilities
    {
        public string ProviderName { get; init; } = string.Empty;

        public bool RequiresApiKey { get; init; }

        public bool SupportsTransforms { get; init; }

        public bool SupportsUsageInclude { get; init; }

        public bool SupportsToolChoice { get; init; } = true;

        public bool SupportsPricing { get; init; }

        public bool SupportsCaching { get; init; }

        public string DefaultModel { get; init; } = string.Empty;

        public TimeSpan ModelListCacheDuration { get; init; } = TimeSpan.FromMinutes(30);

        public IReadOnlyList<ModelInfo> FallbackModels { get; init; } = Array.Empty<ModelInfo>();
    }
}
