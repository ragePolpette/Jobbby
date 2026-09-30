using Config;
using Reporting;

namespace JobPostings;

/// <summary>
/// Fixed-behavior test double for <see cref="IJobSource"/>, mirroring
/// <c>MockLlmClient</c>/<c>MockWebSearchClient</c>: returns the same postings regardless
/// of which source is asked (or, when built from a per-query map, the postings for that
/// query), and records every (source, query, cursor) it was asked about.
/// </summary>
public sealed class MockJobSource : IJobSource
{
    private readonly IReadOnlyList<RawPosting> _postings;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<RawPosting>>? _postingsByQuery;

    public List<SourceDefinition> Requests { get; } = new();

    public List<string> QueriesReceived { get; } = new();

    public List<SourceCursor?> CursorsReceived { get; } = new();

    public MockJobSource(IReadOnlyList<RawPosting> postings)
    {
        _postings = postings;
    }

    /// <summary>Returns the postings mapped to each query; a query mapped to null throws, simulating a failing fetch.</summary>
    public MockJobSource(IReadOnlyDictionary<string, IReadOnlyList<RawPosting>> postingsByQuery)
    {
        _postings = Array.Empty<RawPosting>();
        _postingsByQuery = postingsByQuery;
    }

    public Task<IReadOnlyList<RawPosting>> FetchAsync(SourceDefinition source, string query, SourceCursor? cursor = null, CancellationToken cancellationToken = default)
    {
        Requests.Add(source);
        QueriesReceived.Add(query);
        CursorsReceived.Add(cursor);

        if (_postingsByQuery is null)
            return Task.FromResult(_postings);

        return _postingsByQuery.TryGetValue(query, out var postings) && postings is not null
            ? Task.FromResult(postings)
            : throw new HttpRequestException($"Simulated fetch failure for '{query}'.");
    }
}
