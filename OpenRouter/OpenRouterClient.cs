using System;
using Saturn.OpenRouter.Http;
using Saturn.OpenRouter.Services;

namespace Saturn.OpenRouter
{
    public sealed class OpenRouterClient : IDisposable
    {
        private readonly HttpClientAdapter _http;

        public OpenRouterOptions Options { get; }

        public ModelsService Models { get; }

        public ProvidersService Providers { get; }

        public CreditsService Credits { get; }

        public GenerationService Generation { get; }

        public CompletionsService Completions { get; }

        public ChatCompletionsService Chat { get; }
        public ChatCompletionsStreamingService ChatStreaming { get; }

        public OpenRouterClient(OpenRouterOptions? options = null)
        {
            Options = options ?? new OpenRouterOptions();

            if (string.IsNullOrWhiteSpace(Options.ApiKey))
            {
                var envKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
                if (!string.IsNullOrWhiteSpace(envKey))
                {
                    Options.ApiKey = envKey;
                }
            }

            _http = new HttpClientAdapter(Options);
            Models = new ModelsService(_http);
            Providers = new ProvidersService(_http);
            Credits = new CreditsService(_http);
            Generation = new GenerationService(_http);
            Completions = new CompletionsService(_http);
            Chat = new ChatCompletionsService(_http);
            ChatStreaming = new ChatCompletionsStreamingService(_http, Options);
        }

        public void Dispose()
        {
            _http.Dispose();
        }
    }
}