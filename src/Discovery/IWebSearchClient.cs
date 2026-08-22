namespace Discovery;

/// <summary>One organic web search result.</summary>
public sealed record SearchResult(string Title, string Url, string Snippet);

/// <summary>
/// Provider-agnostic web search seam, mirroring how <c>ILlmClient</c> decouples
/// GraphEngine nodes from a specific LLM provider. See <see cref="BraveSearchClient"/>
/// for the real implementation and <see cref="MockWebSearchClient"/> for tests.
/// </summary>
public interface IWebSearchClient
{
    Task<IReadOnlyList<SearchResult>> SearchAsync(string query, CancellationToken cancellationToken = default);
}
