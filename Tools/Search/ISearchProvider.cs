using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Saturn.Providers;

namespace Saturn.Tools.Search
{
    /// <summary>
    /// A web search backend (Tavily, Brave, Serper, SerpAPI, Exa). Mirrors the LLM
    /// <see cref="ILlmProvider"/> shape so the settings UI and secret persistence can be reused.
    /// </summary>
    public interface ISearchProvider
    {
        string Name { get; }

        string DisplayName { get; }

        IReadOnlyList<ProviderSettingDescriptor> SettingDescriptors { get; }

        Task<SearchResponse> SearchAsync(
            string query,
            int maxResults,
            ProviderSettings settings,
            HttpClient http,
            CancellationToken cancellationToken = default);
    }
}
