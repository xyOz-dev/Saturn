using System;
using System.Collections.Generic;

namespace Saturn.Providers
{
    /// <summary>
    /// String-keyed settings bag for one provider, persisted in agent-config.json.
    /// Keys are defined by each provider's <see cref="ProviderSettingDescriptor"/> list.
    /// </summary>
    public sealed class ProviderSettings
    {
        public Dictionary<string, string?> Values { get; init; } = new(StringComparer.OrdinalIgnoreCase);

        public string? Get(string key) => Values.TryGetValue(key, out var value) ? value : null;

        public void Set(string key, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                Values.Remove(key);
            }
            else
            {
                Values[key] = value;
            }
        }

        public ProviderSettings Clone()
        {
            var clone = new ProviderSettings();
            foreach (var kvp in Values)
            {
                clone.Values[kvp.Key] = kvp.Value;
            }
            return clone;
        }
    }
}
