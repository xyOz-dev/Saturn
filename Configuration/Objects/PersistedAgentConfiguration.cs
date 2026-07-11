using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Saturn.Configuration.Objects
{
    public class PersistedAgentConfiguration
    {
        public string? Name { get; set; }
        public string? Model { get; set; }
        public double? Temperature { get; set; }
        public int? MaxTokens { get; set; }
        public double? TopP { get; set; }
        public bool EnableStreaming { get; set; } = true;
        public bool MaintainHistory { get; set; } = true;
        public int? MaxHistoryMessages { get; set; }
        public bool EnableTools { get; set; } = true;
        public List<string>? ToolNames { get; set; }
        public bool? RequireCommandApproval { get; set; }
        public bool? EnableUserRules { get; set; }

        /// <summary>Registry name of the provider to connect at startup (e.g. "openrouter", "lmstudio").</summary>
        public string? ActiveProvider { get; set; }

        /// <summary>
        /// Per-provider settings and model memory, keyed by provider registry name.
        /// Kept separately from the flat <see cref="Model"/> so switching back and forth
        /// between providers restores the model used on each side.
        /// </summary>
        public Dictionary<string, PersistedProviderConfiguration>? Providers { get; set; }
    }

    public class PersistedProviderConfiguration
    {
        public Dictionary<string, string?>? Settings { get; set; }
        public string? Model { get; set; }
    }
}
