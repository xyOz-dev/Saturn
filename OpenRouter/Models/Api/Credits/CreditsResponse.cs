using System.Text.Json;

namespace Saturn.OpenRouter.Models.Api.Credits
{
    /// <summary>
    /// Typed-light envelope for GET /credits responses.
    /// The OpenRouter /credits schema may evolve; this DTO exposes best-effort totals
    /// and always retains the full JSON payload in <see cref="Root"/> for forward compatibility.
    ///
    /// Schema notes:
    /// - Totals, when present, appear under the top-level "data" object:
    ///   - data.total_credits
    ///   - data.total_usage
    /// - <see cref="Data"/> contains the optional "data" object if present.
    /// - <see cref="Root"/> contains the full response JSON.
    /// </summary>
    public sealed class CreditsResponse
    {
        /// <summary>
        /// Optional total credits purchased (USD), mapped from data.total_credits when present.
        /// </summary>
        public decimal? TotalCredits { get; set; }

        /// <summary>
        /// Optional total usage/spend to date (USD), mapped from data.total_usage when present.
        /// </summary>
        public decimal? TotalUsage { get; set; }

        /// <summary>
        /// The optional top-level "data" object, if present; otherwise null.
        /// </summary>
        public JsonElement? Data { get; set; }

        /// <summary>
        /// The entire JSON response for forward compatibility.
        /// </summary>
        public JsonElement Root { get; set; }
    }
}