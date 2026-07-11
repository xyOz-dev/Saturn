using System;

namespace Saturn.Providers
{
    public enum ProviderSettingKind
    {
        Text,
        Secret,
        Url,
        Number
    }

    /// <summary>
    /// Describes one configurable setting of a provider so generic UI can render an
    /// input field for it and resolution can fall back to environment variables.
    /// </summary>
    public sealed class ProviderSettingDescriptor
    {
        public required string Key { get; init; }
        public required string Label { get; init; }
        public ProviderSettingKind Kind { get; init; } = ProviderSettingKind.Text;
        public bool Required { get; init; }
        public string? DefaultValue { get; init; }

        /// <summary>Environment variable consulted when the setting is absent from saved config.</summary>
        public string? EnvironmentVariable { get; init; }

        /// <summary>
        /// Resolves the effective value: explicit setting first, then environment
        /// variable, then the descriptor default.
        /// </summary>
        public string? Resolve(ProviderSettings? settings)
        {
            var configured = settings?.Get(Key);
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured;
            }

            if (!string.IsNullOrWhiteSpace(EnvironmentVariable))
            {
                var fromEnv = Environment.GetEnvironmentVariable(EnvironmentVariable);
                if (!string.IsNullOrWhiteSpace(fromEnv))
                {
                    return fromEnv;
                }
            }

            return DefaultValue;
        }
    }
}
