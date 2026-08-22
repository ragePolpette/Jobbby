namespace Discovery;

/// <summary>
/// Test double for <see cref="IWebSearchClient"/>, mirroring <c>MockLlmClient</c>: no
/// real HTTP call, results come from a caller-supplied function keyed by the query text
/// (so a test can return different results for the intent search vs. each per-candidate
/// reputation search). Every query received is recorded in <see cref="Queries"/> so tests
/// can assert on how they were composed.
/// </summary>
public sealed class MockWebSearchClient : IWebSearchClient
{
    private readonly Func<string, IReadOnlyList<SearchResult>> _responder;

    public List<string> Queries { get; } = new();

    public MockWebSearchClient(Func<string, IReadOnlyList<SearchResult>> responder)
    {
        _responder = responder;
    }

    /// <summary>Convenience overload: always returns the same fixed results regardless of query.</summary>
    public MockWebSearchClient(IReadOnlyList<SearchResult> fixedResults)
        : this(_ => fixedResults)
    {
    }

    public Task<IReadOnlyList<SearchResult>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        Queries.Add(query);
        return Task.FromResult(_responder(query));
    }
}
