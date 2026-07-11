using System;

namespace Saturn.Providers
{
    /// <summary>
    /// How consumers obtain the currently active client. Nothing outside the provider
    /// layer should hold an <see cref="ILlmClient"/> across turns; resolving through
    /// this source is what makes providers hot-swappable mid-session.
    /// </summary>
    public interface ILlmClientSource
    {
        /// <summary>The active client. Throws if no provider has been connected yet.</summary>
        ILlmClient Current { get; }

        bool IsConnected { get; }

        /// <summary>Registry name of the active provider (e.g. "openrouter"), or empty when disconnected.</summary>
        string ActiveProviderName { get; }

        event EventHandler<ProviderChangedEventArgs>? ProviderChanged;
    }

    public sealed class ProviderChangedEventArgs : EventArgs
    {
        public ProviderChangedEventArgs(string previousProviderName, string newProviderName, ILlmClient newClient)
        {
            PreviousProviderName = previousProviderName;
            NewProviderName = newProviderName;
            NewClient = newClient;
        }

        public string PreviousProviderName { get; }
        public string NewProviderName { get; }
        public ILlmClient NewClient { get; }
    }

    /// <summary>Fixed, non-swappable source wrapping a single client. Useful for tests.</summary>
    public sealed class StaticClientSource : ILlmClientSource
    {
        private readonly ILlmClient _client;

        public StaticClientSource(ILlmClient client, string providerName = "static")
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            ActiveProviderName = providerName;
        }

        public ILlmClient Current => _client;
        public bool IsConnected => true;
        public string ActiveProviderName { get; }

        public event EventHandler<ProviderChangedEventArgs>? ProviderChanged
        {
            add { }
            remove { }
        }
    }
}
