using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Saturn.Providers
{
    /// <summary>
    /// The plugin contract: describes a provider and constructs connected clients for it.
    /// Register implementations with <see cref="ProviderRegistry"/> at startup.
    /// </summary>
    public interface ILlmProvider
    {
        /// <summary>Stable lowercase identifier used in config files and SATURN_PROVIDER (e.g. "lmstudio").</summary>
        string Name { get; }

        string DisplayName { get; }

        IReadOnlyList<ProviderSettingDescriptor> SettingDescriptors { get; }

        /// <summary>
        /// Builds an unvalidated client from the given settings. Throws with an actionable
        /// message when a required setting is missing.
        /// </summary>
        Task<ILlmClient> CreateClientAsync(ProviderSettings settings, CancellationToken cancellationToken = default);
    }
}
