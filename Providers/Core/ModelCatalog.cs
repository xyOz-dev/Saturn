using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Saturn.Providers
{
    public static class ModelCatalog
    {
        private static readonly object _lock = new();
        private static readonly Dictionary<string, (List<ModelInfo> Models, DateTime FetchedAt)> _cache =
            new(StringComparer.OrdinalIgnoreCase);

        public static async Task<List<ModelInfo>> GetAsync(ILlmClientSource clientSource, CancellationToken cancellationToken = default)
        {
            if (clientSource == null || !clientSource.IsConnected)
            {
                return new List<ModelInfo>();
            }

            string providerKey;
            ILlmClient client;
            while (true)
            {
                providerKey = clientSource.ActiveProviderName;
                client = clientSource.Current;
                if (clientSource.ActiveProviderName == providerKey)
                {
                    break;
                }
            }

            lock (_lock)
            {
                if (_cache.TryGetValue(providerKey, out var cached) &&
                    DateTime.UtcNow - cached.FetchedAt < client.Capabilities.ModelListCacheDuration)
                {
                    return cached.Models;
                }
            }

            try
            {
                var models = await client.ListModelsAsync(cancellationToken);
                if (models.Count > 0)
                {
                    lock (_lock)
                    {
                        _cache[providerKey] = (models, DateTime.UtcNow);
                    }
                    return models;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
            }

            return client.Capabilities.FallbackModels.ToList();
        }

        public static async Task<string> ResolveModelAsync(ILlmClientSource clientSource, string requestedModel, string? preferredFallback = null)
        {
            try
            {
                var models = await GetAsync(clientSource);
                if (models.Count == 0)
                {
                    return requestedModel;
                }

                if (models.Any(m => string.Equals(m.Id, requestedModel, StringComparison.OrdinalIgnoreCase)))
                {
                    return requestedModel;
                }

                var variantSeparator = requestedModel.LastIndexOf(':');
                if (variantSeparator > 0)
                {
                    var baseModel = requestedModel.Substring(0, variantSeparator);
                    if (models.Any(m => string.Equals(m.Id, baseModel, StringComparison.OrdinalIgnoreCase)))
                    {
                        return requestedModel;
                    }
                }

                if (!string.IsNullOrWhiteSpace(preferredFallback) &&
                    models.Any(m => string.Equals(m.Id, preferredFallback, StringComparison.OrdinalIgnoreCase)))
                {
                    return preferredFallback!;
                }

                var defaultModel = clientSource.Current.Capabilities.DefaultModel;
                if (!string.IsNullOrWhiteSpace(defaultModel) &&
                    models.Any(m => string.Equals(m.Id, defaultModel, StringComparison.OrdinalIgnoreCase)))
                {
                    return defaultModel;
                }

                return models.FirstOrDefault(m => m.IsLoaded == true)?.Id ?? models[0].Id;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return requestedModel;
            }
        }

        public static void Invalidate()
        {
            lock (_lock)
            {
                _cache.Clear();
            }
        }
    }
}
