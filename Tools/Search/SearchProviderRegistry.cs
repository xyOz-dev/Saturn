using System;
using System.Collections.Generic;
using System.Linq;
using Saturn.Tools.Search.Providers;

namespace Saturn.Tools.Search
{
    public static class SearchProviderRegistry
    {
        private static readonly object _lock = new();
        private static readonly Dictionary<string, ISearchProvider> _providers = new(StringComparer.OrdinalIgnoreCase);

        static SearchProviderRegistry()
        {
            Register(new TavilySearchProvider());
            Register(new BraveSearchProvider());
            Register(new SerperSearchProvider());
            Register(new SerpApiSearchProvider());
            Register(new ExaSearchProvider());
        }

        public static void Register(ISearchProvider provider)
        {
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            lock (_lock)
            {
                _providers[provider.Name] = provider;
            }
        }

        public static bool TryGet(string name, out ISearchProvider provider)
        {
            lock (_lock)
            {
                return _providers.TryGetValue(name ?? string.Empty, out provider!);
            }
        }

        public static ISearchProvider Get(string name)
        {
            if (TryGet(name, out var provider))
            {
                return provider;
            }

            lock (_lock)
            {
                var known = _providers.Count == 0 ? "(none registered)" : string.Join(", ", _providers.Keys);
                throw new InvalidOperationException($"Unknown search provider '{name}'. Available providers: {known}.");
            }
        }

        public static IReadOnlyList<ISearchProvider> All
        {
            get
            {
                lock (_lock)
                {
                    return _providers.Values.OrderBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
                }
            }
        }

        public static void Clear()
        {
            lock (_lock)
            {
                _providers.Clear();
            }
        }
    }
}
