using System.Text.Json;

namespace Saturn.OpenRouter.Models.Api.Providers
{
    public sealed class ProvidersResponse
    {
        public JsonElement Root { get; set; }

        public JsonElement? Data { get; set; }
    }
}