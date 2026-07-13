using System;

namespace Saturn.Providers
{
    public interface ILlmClientSource
    {
        ILlmClient Current { get; }

        bool IsConnected { get; }

        string ActiveProviderName { get; }

        /// <summary>
        /// Returns a consistent (provider name, client) pair even while a provider swap is in flight.
        /// </summary>
        (string ProviderName, ILlmClient Client) Snapshot() => (ActiveProviderName, Current);

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
