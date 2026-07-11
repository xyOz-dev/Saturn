using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Saturn.OpenRouter;

namespace Saturn.Providers
{
    public sealed class OpenRouterProvider : ILlmProvider
    {
        public const string ProviderName = "openrouter";
        public const string ApiKeySetting = "apiKey";

        public string Name => ProviderName;
        public string DisplayName => "OpenRouter";

        public IReadOnlyList<ProviderSettingDescriptor> SettingDescriptors { get; } = new[]
        {
            new ProviderSettingDescriptor
            {
                Key = ApiKeySetting,
                Label = "API Key",
                Kind = ProviderSettingKind.Secret,
                Required = true,
                EnvironmentVariable = "OPENROUTER_API_KEY"
            }
        };

        public Task<ILlmClient> CreateClientAsync(ProviderSettings settings, CancellationToken cancellationToken = default)
        {
            var apiKey = SettingDescriptors[0].Resolve(settings);
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException(
                    "OpenRouter requires an API key. Set the OPENROUTER_API_KEY environment variable, " +
                    "or switch to a local provider with SATURN_PROVIDER=lmstudio.");
            }

            var client = new OpenRouterClient(new OpenRouterOptions
            {
                ApiKey = apiKey,
                Referer = "https://github.com/xyOz-dev/Saturn",
                Title = "Saturn"
            });

            return Task.FromResult<ILlmClient>(new OpenRouterLlmClient(client));
        }
    }
}
