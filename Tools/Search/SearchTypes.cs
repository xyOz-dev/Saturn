using System.Collections.Generic;

namespace Saturn.Tools.Search
{
    public sealed record SearchResult(string Title, string Url, string Snippet);

    public sealed record SearchResponse(IReadOnlyList<SearchResult> Results, string Provider);
}
