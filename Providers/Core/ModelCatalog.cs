using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Saturn.Providers
{
    /// <summary>
    /// Shared, provider-keyed cache of model listings. Keyed per provider so swapping
    /// never surfaces a previous provider's models, with each provider declaring its own
    /// freshness window (local servers change their list whenever the user loads or
    /// unloads a model).
    /// </summary>
    public static class ModelCatalog
    {
        private static readonly object _lock = new();
        private static readonly Dictionary<string, (List<ModelInfo> Models, DateTime FetchedAt)> _cache =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Lists models for the active provider, from cache when fresh. Falls back to the
        /// provider's static fallback list when the live listing fails or is empty.
        /// </summary>
        public static async Task<List<ModelInfo>> GetAsync(ILlmClientSource clientSource, CancellationToken cancellationToken = default)
        {
            if (clientSource == null || !clientSource.IsConnected)
            {
                return new List<ModelInfo>();
            }

            // The two reads on the source are not atomic across a concurrent swap; re-read
            // the name until it brackets the client read consistently, so a list can never
            // be cached under the wrong provider's key.
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

        /// <summary>
        /// Returns <paramref name="requestedModel"/> when the active provider offers it;
        /// otherwise the best available substitute (a loaded model first, then the
        /// provider default, then the first listed). Lets sub-agent requests written for
        /// one provider degrade gracefully after a swap instead of erroring per request.
        /// </summary>
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

                // OpenRouter routing variants ("vendor/model:nitro", ":online", ":free")
                // are valid request ids that never appear verbatim in the models listing;
                // when the base model exists, trust the requested variant.
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
