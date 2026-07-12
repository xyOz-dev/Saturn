using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Saturn.Providers
{
    public sealed class LMStudioProvider : ILlmProvider
    {
        public const string ProviderName = "lmstudio";
        public const string BaseUrlSetting = "baseUrl";
        public const string TimeoutSetting = "timeoutSeconds";

        private const int DefaultTimeoutSeconds = 600;

        public string Name => ProviderName;
        public string DisplayName => "LM Studio";

        public IReadOnlyList<ProviderSettingDescriptor> SettingDescriptors { get; } = new[]
        {
            new ProviderSettingDescriptor
            {
                Key = BaseUrlSetting,
                Label = "Base URL",
                Kind = ProviderSettingKind.Url,
                Required = true,
                DefaultValue = "http://localhost:1234/v1",
                EnvironmentVariable = "LMSTUDIO_BASE_URL"
            },
            new ProviderSettingDescriptor
            {
                Key = TimeoutSetting,
                Label = "Request timeout (seconds)",
                Kind = ProviderSettingKind.Number,
                DefaultValue = DefaultTimeoutSeconds.ToString()
            }
        };

        public Task<ILlmClient> CreateClientAsync(ProviderSettings settings, CancellationToken cancellationToken = default)
        {
            var baseUrl = SettingDescriptors.First(d => d.Key == BaseUrlSetting).Resolve(settings);
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                throw new InvalidOperationException("LM Studio requires a base URL (default: http://localhost:1234/v1).");
            }

            var timeoutText = SettingDescriptors.First(d => d.Key == TimeoutSetting).Resolve(settings);
            if (!int.TryParse(timeoutText, out var timeoutSeconds) || timeoutSeconds <= 0)
            {
                timeoutSeconds = DefaultTimeoutSeconds;
            }

            return Task.FromResult<ILlmClient>(new LMStudioClient(baseUrl, TimeSpan.FromSeconds(timeoutSeconds)));
        }
    }
}
