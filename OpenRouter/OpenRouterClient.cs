using System;
using Saturn.OpenRouter.Http;
using Saturn.OpenRouter.Services;

namespace Saturn.OpenRouter
{
    /// <summary>
    /// Root facade client for the OpenRouter SDK.
    /// Holds <see cref="OpenRouterOptions"/> and an internal HTTP adapter,
    /// and exposes the Models service via <see cref="Models"/>.
    ///
    /// Example usage:
    /// <code>
    /// // using Saturn.OpenRouter;
    /// // using Saturn.OpenRouter.Services;
    /// //
    /// // var client = new OpenRouterClient(new OpenRouterOptions
    /// // {
    /// //     // Optional: if not set, the client will read OPENROUTER_API_KEY from the environment
    /// //     // ApiKey = "sk-or-...",
    /// //     Referer = "https://your-app.example",
    /// //     Title = "Your App Name"
    /// // });
    /// //
    /// // var list = await client.Models.ListAllAsync();
    /// // foreach (var m in list?.Data ?? Array.Empty<Models.Api.Models.Model>())
    /// // {
    /// //     Console.WriteLine(m.Id);
    /// // }
    /// </code>
    /// </summary>
    public sealed class OpenRouterClient : IDisposable
    {
        private readonly HttpClientAdapter _http;

        /// <summary>
        /// Gets the options used by this client instance.
        /// </summary>
        public OpenRouterOptions Options { get; }

        /// <summary>
        /// Service for interacting with the Models API.
        /// </summary>
        public ModelsService Models { get; }

        /// <summary>
        /// Service for listing Providers (GET /providers).
        ///
        /// Example usage:
        /// <code>
        /// // using Saturn.OpenRouter;
        /// // var client = new OpenRouterClient(new OpenRouterOptions
        /// // {
        /// //     Referer = "https://your-app.example",
        /// //     Title = "Your App Name"
        /// // });
        /// //
        /// // var ct = new System.Threading.CancellationTokenSource().Token;
        /// // Saturn.OpenRouter.Models.Api.Providers.ProvidersResponse providers = await client.Providers.ListAsync(ct);
        /// //
        /// // // Or raw JSON if you need complete control:
        /// // using var raw = await client.Providers.ListRawAsync(ct);
        /// // var root = raw.RootElement;
        /// </code>
        /// </summary>
        public ProvidersService Providers { get; }

        /// <summary>
        /// Service for retrieving account credits totals (GET /credits).
        ///
        /// Example usage:
        /// <code>
        /// // using Saturn.OpenRouter;
        /// //
        /// // var client = new OpenRouterClient(new OpenRouterOptions
        /// // {
        /// //     Referer = "https://your-app.example",
        /// //     Title = "Your App Name"
        /// // });
        /// //
        /// // var ct = new System.Threading.CancellationTokenSource().Token;
        /// // Saturn.OpenRouter.Models.Api.Credits.CreditsResponse credits = await client.Credits.GetAsync(ct);
        /// // var totalCredits = credits.TotalCredits; // decimal?
        /// // var totalUsage = credits.TotalUsage;     // decimal?
        /// //
        /// // // Or raw JSON if you need complete control:
        /// // using var raw = await client.Credits.GetRawAsync(ct);
        /// // var root = raw.RootElement;
        /// </code>
        /// </summary>
        public CreditsService Credits { get; }

        /// <summary>
        /// Service for retrieving detailed generation usage and cost stats (GET /generation?id=...).
        ///
        /// Example usage:
        /// <code>
        /// // using Saturn.OpenRouter;
        /// //
        /// // var client = new OpenRouterClient(new OpenRouterOptions
        /// // {
        /// //     Referer = "https://your-app.example",
        /// //     Title = "Your App Name"
        /// // });
        /// //
        /// // var ct = new System.Threading.CancellationTokenSource().Token;
        /// // var gen = await client.Generation.GetAsync("gen-123", ct);
        /// // var usage = gen.Usage; // Saturn.OpenRouter.Models.Api.Common.ResponseUsage?
        /// //
        /// // // Or raw JSON if you need complete control:
        /// // using var raw = await client.Generation.GetRawAsync("gen-123", ct);
        /// // var root = raw.RootElement;
        /// </code>
        /// </summary>
        public GenerationService Generation { get; }

        /// <summary>
        /// Service for non-streaming Text Completions (POST /completions).
        ///
        /// Example usage:
        /// <code>
        /// // using Saturn.OpenRouter;
        /// // using Saturn.OpenRouter.Models.Api.Completions;
        /// //
        /// // var client = new OpenRouterClient(new OpenRouterOptions
        /// // {
        /// //     Referer = "https://your-app.example",
        /// //     Title = "Your App Name"
        /// // });
        /// //
        /// // var request = new TextCompletionRequest
        /// // {
        /// //     Model = "openai/gpt-3.5-turbo-instruct",
        /// //     Prompt = "Write a haiku about the sea",
        /// //     MaxTokens = 64
        /// // };
        /// //
        /// // TextCompletionResponse? response = await client.Completions.CreateAsync(request);
        /// // // Saturn.OpenRouter.Models.Api.Completions.TextCompletionResponse
        /// </code>
        /// </summary>
        public CompletionsService Completions { get; }

        /// <summary>
        /// Service for non-streaming Chat Completions (POST /chat/completions).
        ///
        /// Example usage:
        /// <code>
        /// // using Saturn.OpenRouter;
        /// // using Saturn.OpenRouter.Models.Api.Chat;
        /// //
        /// // var client = new OpenRouterClient(new OpenRouterOptions
        /// // {
        /// //     Referer = "https://your-app.example",
        /// //     Title = "Your App Name"
        /// // });
        /// //
        /// // var request = new ChatCompletionRequest
        /// // {
        /// //     Model = "openai/gpt-4o-mini",
        /// //     Messages = new[]
        /// //     {
        /// //         new Message { Role = "user", Content = System.Text.Json.JsonDocument.Parse("\"Hello\"").RootElement }
        /// //     }
        /// // };
        /// //
        /// // var response = await client.Chat.CreateAsync(request);
        /// // // Saturn.OpenRouter.Models.Api.Chat.ChatCompletionResponse
        /// </code>
        /// </summary>
        public ChatCompletionsService Chat { get; }
/// <summary>
        /// Service for streaming Chat Completions over SSE (POST /chat/completions with stream=true).
        ///
        /// Example usage:
        /// <code>
        /// // using Saturn.OpenRouter;
        /// // using Saturn.OpenRouter.Models.Api.Chat;
        /// //
        /// // var client = new OpenRouterClient(new OpenRouterOptions
        /// // {
        /// //     Referer = "https://your-app.example",
        /// //     Title = "Your App Name"
        /// // });
        /// //
        /// // var request = new ChatCompletionRequest
        /// // {
        /// //     Model = "openai/gpt-4o-mini",
        /// //     Messages = new[]
        /// //     {
        /// //         new Message { Role = "user", Content = System.Text.Json.JsonDocument.Parse("\"Hello\"").RootElement }
        /// //     }
        /// //     // Stream flag will be forced to true by the streaming service.
        /// // };
        /// //
        /// // var cts = new System.Threading.CancellationTokenSource();
        /// // var ct = cts.Token;
        /// // await foreach (var chunk in client.ChatStreaming.StreamAsync(request, ct))
        /// // {
        /// //     // chunk.Choices may contain deltas (including tool_calls).
        /// //     // chunk.Usage may appear at the end with empty choices.
        /// // }
        /// </code>
        /// </summary>
        public ChatCompletionsStreamingService ChatStreaming { get; }

        /// <summary>
        /// Create a new OpenRouter client with the given options.
        /// If <paramref name="options"/> is null, defaults are used.
        /// If no API key is provided in options, the client attempts to read OPENROUTER_API_KEY from the environment.
        /// </summary>
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

        /// <summary>
        /// Dispose the underlying HTTP resources.
        /// </summary>
        public void Dispose()
        {
            _http.Dispose();
        }
    }
}