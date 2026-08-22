using Config;

namespace JobPostings;

/// <summary>
/// Fixed-behavior test double for <see cref="IJobSource"/>, mirroring
/// <c>MockLlmClient</c>/<c>MockWebSearchClient</c>: returns the same postings regardless
/// of which source is asked, and records every source it was asked about.
/// </summary>
public sealed class MockJobSource : IJobSource
{
    private readonly IReadOnlyList<RawPosting> _postings;

    public List<SourceDefinition> Requests { get; } = new();

    public MockJobSource(IReadOnlyList<RawPosting> postings)
    {
        _postings = postings;
    }

    public Task<IReadOnlyList<RawPosting>> FetchAsync(SourceDefinition source, CancellationToken cancellationToken = default)
    {
        Requests.Add(source);
        return Task.FromResult(_postings);
    }
}
