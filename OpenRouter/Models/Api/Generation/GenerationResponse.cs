using System.Text.Json;
using Saturn.OpenRouter.Models.Api.Common;

namespace Saturn.OpenRouter.Models.Api.Generation
{
    public sealed class GenerationResponse
    {
        public string? Id { get; set; }

        public string? Model { get; set; }

        public string? Provider { get; set; }

        public long? Created { get; set; }

        public ResponseUsage? Usage { get; set; }

        public JsonElement Root { get; set; }
    }
}