using Config;
using Reporting;

namespace JobPostings;

/// <summary>
/// Fixed-behavior test double for <see cref="IJobSource"/>, mirroring
/// <c>MockLlmClient</c>/<c>MockWebSearchClient</c>: returns the same postings regardless
/// of which source is asked, and records every (source, cursor) pair it was asked about.
/// </summary>
public sealed class MockJobSource : IJobSource
{
    private readonly IReadOnlyList<RawPosting> _postings;

    public List<SourceDefinition> Requests { get; } = new();

    public List<SourceCursor?> CursorsReceived { get; } = new();

    public MockJobSource(IReadOnlyList<RawPosting> postings)
    {
        _postings = postings;
    }

    public Task<IReadOnlyList<RawPosting>> FetchAsync(SourceDefinition source, SourceCursor? cursor = null, CancellationToken cancellationToken = default)
    {
        Requests.Add(source);
        CursorsReceived.Add(cursor);
        return Task.FromResult(_postings);
    }
}
