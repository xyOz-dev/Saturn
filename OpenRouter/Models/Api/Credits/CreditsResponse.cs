using System.Text.Json;

namespace Saturn.OpenRouter.Models.Api.Credits
{
    public sealed class CreditsResponse
    {
        public decimal? TotalCredits { get; set; }

        public decimal? TotalUsage { get; set; }

        public JsonElement? Data { get; set; }

        public JsonElement Root { get; set; }
    }
}