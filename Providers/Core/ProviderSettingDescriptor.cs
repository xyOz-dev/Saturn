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

    public sealed class ProviderSettingDescriptor
    {
        public required string Key { get; init; }
        public required string Label { get; init; }
        public ProviderSettingKind Kind { get; init; } = ProviderSettingKind.Text;
        public bool Required { get; init; }
        public string? DefaultValue { get; init; }

        public string? EnvironmentVariable { get; init; }

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
