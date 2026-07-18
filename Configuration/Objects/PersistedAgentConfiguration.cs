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
        public bool? EnableSkills { get; set; }

        public string? ActiveProvider { get; set; }

        public Dictionary<string, PersistedProviderConfiguration>? Providers { get; set; }

        public string? SearchProvider { get; set; }

        public Dictionary<string, PersistedProviderConfiguration>? SearchProviders { get; set; }
    }

    public class PersistedProviderConfiguration
    {
        public Dictionary<string, string?>? Settings { get; set; }
        public string? Model { get; set; }
    }
}
