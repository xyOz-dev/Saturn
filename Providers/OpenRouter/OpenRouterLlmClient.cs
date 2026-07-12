using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Saturn.OpenRouter;
using Saturn.OpenRouter.Models.Api.Chat;

namespace Saturn.Providers
{
    public sealed class OpenRouterLlmClient : ILlmClient
    {
        private readonly OpenRouterClient _inner;

        public OpenRouterLlmClient(OpenRouterClient inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public OpenRouterClient Inner => _inner;

        public LlmClientCapabilities Capabilities { get; } = new()
        {
            ProviderName = "OpenRouter",
            RequiresApiKey = true,
            SupportsTransforms = true,
            SupportsUsageInclude = true,
            SupportsToolChoice = true,
            SupportsPricing = true,
            SupportsCaching = true,
            DefaultModel = "anthropic/claude-sonnet-4",
            ModelListCacheDuration = TimeSpan.FromMinutes(30),
            FallbackModels = new[]
            {
                new ModelInfo { Id = "openai/gpt-5", DisplayName = "OpenAI: GPT-5" },
                new ModelInfo { Id = "openai/gpt-5-mini", DisplayName = "OpenAI: GPT-5-Mini" },
                new ModelInfo { Id = "openai/gpt-5-nano", DisplayName = "OpenAI: GPT-5-Nano" },
                new ModelInfo { Id = "openai/gpt-oss-120b", DisplayName = "OpenAI: GPT-OSS-120B" },
                new ModelInfo { Id = "openai/gpt-oss-20b", DisplayName = "OpenAI: GPT-OSS-20B" },
                new ModelInfo { Id = "anthropic/claude-opus-4.1", DisplayName = "Anthropic: Opus-4.1" },
                new ModelInfo { Id = "anthropic/claude-opus-4", DisplayName = "Anthropic: Opus-4" },
                new ModelInfo { Id = "anthropic/claude-sonnet-4", DisplayName = "Anthropic: Sonnet-4" },
                new ModelInfo { Id = "anthropic/claude-3.7-sonnet", DisplayName = "Anthropic: Sonnet-3.7" },
                new ModelInfo { Id = "anthropic/claude-3.5-haiku", DisplayName = "Anthropic: Haiku-3.5" },
                new ModelInfo { Id = "moonshotai/kimi-k2:free", DisplayName = "Kimi-K2" }
            }
        };

        public Task<ChatCompletionResponse?> ChatAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default)
            => _inner.Chat.CreateAsync(request, cancellationToken);

        public IAsyncEnumerable<ChatCompletionChunk> StreamChatAsync(ChatCompletionRequest request, CancellationToken cancellationToken = default)
            => _inner.ChatStreaming.StreamAsync(request, cancellationToken);

        public async Task<List<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
        {
            var response = await _inner.Models.ListAllAsync(cancellationToken);
            if (response?.Data == null)
            {
                return new List<ModelInfo>();
            }

            return response.Data
                .Where(m => !string.IsNullOrEmpty(m.Id))
                .Select(m => new ModelInfo
                {
                    Id = m.Id!,
                    DisplayName = m.Name,
                    Description = m.Description,
                    ContextLength = m.ContextLength,
                    PromptPrice = m.Pricing?.Prompt,
                    CompletionPrice = m.Pricing?.Completion
                })
                .OrderBy(m => m.Display, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public async Task<bool> ValidateConnectionAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                await _inner.Credits.GetAsync(cancellationToken);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void Dispose() => _inner.Dispose();
    }
}
