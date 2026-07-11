using System;
using System.Collections.Generic;
using System.Linq;

namespace Saturn.Providers
{
    /// <summary>
    /// Name-to-provider lookup. Adding a provider to Saturn means implementing
    /// <see cref="ILlmProvider"/> and registering it here during startup.
    /// </summary>
    public static class ProviderRegistry
    {
        private static readonly object _lock = new();
        private static readonly Dictionary<string, ILlmProvider> _providers = new(StringComparer.OrdinalIgnoreCase);

        public static void Register(ILlmProvider provider)
        {
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            lock (_lock)
            {
                _providers[provider.Name] = provider;
            }
        }

        public static bool TryGet(string name, out ILlmProvider provider)
        {
            lock (_lock)
            {
                return _providers.TryGetValue(name ?? string.Empty, out provider!);
            }
        }

        public static ILlmProvider Get(string name)
        {
            if (TryGet(name, out var provider))
            {
                return provider;
            }

            lock (_lock)
            {
                var known = _providers.Count == 0 ? "(none registered)" : string.Join(", ", _providers.Keys);
                throw new InvalidOperationException($"Unknown provider '{name}'. Available providers: {known}.");
            }
        }

        public static IReadOnlyList<ILlmProvider> All
        {
            get
            {
                lock (_lock)
                {
                    return _providers.Values.OrderBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
                }
            }
        }

        /// <summary>Removes all registrations. Intended for test isolation only.</summary>
        public static void Clear()
        {
            lock (_lock)
            {
                _providers.Clear();
            }
        }
    }
}
