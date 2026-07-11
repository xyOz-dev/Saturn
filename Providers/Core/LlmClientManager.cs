using System;
using System.Threading;
using System.Threading.Tasks;

namespace Saturn.Providers
{
    public sealed class SwapResult
    {
        private SwapResult(bool success, string? error, ILlmClient? client)
        {
            Success = success;
            Error = error;
            Client = client;
        }

        public bool Success { get; }
        public string? Error { get; }
        public ILlmClient? Client { get; }

        public static SwapResult Ok(ILlmClient client) => new(true, null, client);
        public static SwapResult Failed(string error) => new(false, error, null);
    }

    /// <summary>
    /// The swap engine. Builds and validates a candidate client before touching the
    /// active one, so a failed connect leaves the session exactly where it was. The
    /// replaced client is disposed after a grace period because in-flight requests
    /// that resolved it before the swap may still be streaming on it.
    /// </summary>
    public sealed class LlmClientManager : ILlmClientSource, IDisposable
    {
        private static readonly TimeSpan RetiredClientGracePeriod = TimeSpan.FromMinutes(10);

        private readonly object _swapLock = new();
        private ILlmClient? _current;
        private string _activeProviderName = string.Empty;
        private ProviderSettings _activeSettings = new();

        public static LlmClientManager Instance { get; } = new();

        public ILlmClient Current =>
            _current ?? throw new InvalidOperationException(
                "No LLM provider is connected. Connect one via LlmClientManager.SwapAsync before creating agents.");

        public bool IsConnected => _current != null;

        public string ActiveProviderName => _activeProviderName;

        /// <summary>Settings the active client was built with; used to prefill the provider dialog.</summary>
        public ProviderSettings ActiveSettings => _activeSettings.Clone();

        public event EventHandler<ProviderChangedEventArgs>? ProviderChanged;

        public async Task<SwapResult> SwapAsync(string providerName, ProviderSettings settings, CancellationToken cancellationToken = default)
        {
            ILlmProvider provider;
            try
            {
                provider = ProviderRegistry.Get(providerName);
            }
            catch (InvalidOperationException ex)
            {
                return SwapResult.Failed(ex.Message);
            }

            settings ??= new ProviderSettings();

            ILlmClient candidate;
            try
            {
                candidate = await provider.CreateClientAsync(settings, cancellationToken);
            }
            catch (Exception ex)
            {
                return SwapResult.Failed(ex.Message);
            }

            bool reachable;
            try
            {
                reachable = await candidate.ValidateConnectionAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                SafeDispose(candidate);
                return SwapResult.Failed($"Could not connect to {provider.DisplayName}: {ex.Message}");
            }

            if (!reachable)
            {
                SafeDispose(candidate);
                return SwapResult.Failed($"Could not connect to {provider.DisplayName}. Check the provider settings and that the service is reachable.");
            }

            ILlmClient? retired;
            string previousName;
            lock (_swapLock)
            {
                retired = _current;
                previousName = _activeProviderName;
                _current = candidate;
                _activeProviderName = provider.Name;
                _activeSettings = settings.Clone();
            }

            if (retired != null)
            {
                RetireClient(retired);
            }

            ProviderChanged?.Invoke(this, new ProviderChangedEventArgs(previousName, provider.Name, candidate));
            return SwapResult.Ok(candidate);
        }

        private static void RetireClient(ILlmClient client)
        {
            _ = Task.Delay(RetiredClientGracePeriod).ContinueWith(_ => SafeDispose(client));
        }

        private static void SafeDispose(ILlmClient client)
        {
            try
            {
                client.Dispose();
            }
            catch
            {
            }
        }

        public void Dispose()
        {
            ILlmClient? current;
            lock (_swapLock)
            {
                current = _current;
                _current = null;
                _activeProviderName = string.Empty;
            }

            if (current != null)
            {
                SafeDispose(current);
            }
        }
    }
}
