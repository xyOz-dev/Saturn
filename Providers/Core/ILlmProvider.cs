using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Saturn.Providers
{
    public interface ILlmProvider
    {
        string Name { get; }

        string DisplayName { get; }

        IReadOnlyList<ProviderSettingDescriptor> SettingDescriptors { get; }

        Task<ILlmClient> CreateClientAsync(ProviderSettings settings, CancellationToken cancellationToken = default);
    }
}
