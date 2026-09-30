using Config;
using Reporting;

namespace JobPostings;

/// <summary>
/// Fixed-behavior test double for <see cref="IJobSource"/>, mirroring
/// <c>MockLlmClient</c>/<c>MockWebSearchClient</c>: returns the same postings regardless
/// of the request, the postings mapped to the request's query, or whatever a responder
/// function builds from the request; records every (source, request, cursor) it was asked about.
/// </summary>
public sealed class MockJobSource : IJobSource
{
    private readonly Func<JobSearchRequest, IReadOnlyList<RawPosting>> _respond;

    public List<SourceDefinition> Requests { get; } = new();

    public List<JobSearchRequest> RequestsReceived { get; } = new();

    public List<string> QueriesReceived { get; } = new();

    public List<SourceCursor?> CursorsReceived { get; } = new();

    public MockJobSource(IReadOnlyList<RawPosting> postings)
        : this(_ => postings)
    {
    }

    /// <summary>Returns the postings mapped to each query; a query mapped to null or missing throws, simulating a failing fetch.</summary>
    public MockJobSource(IReadOnlyDictionary<string, IReadOnlyList<RawPosting>> postingsByQuery)
        : this(request => postingsByQuery.TryGetValue(request.Query, out var postings) && postings is not null
            ? postings
            : throw new HttpRequestException($"Simulated fetch failure for '{request.Query}'."))
    {
    }

    public MockJobSource(Func<JobSearchRequest, IReadOnlyList<RawPosting>> respond)
    {
        _respond = respond;
    }

    public Task<IReadOnlyList<RawPosting>> FetchAsync(SourceDefinition source, JobSearchRequest request, SourceCursor? cursor = null, CancellationToken cancellationToken = default)
    {
        Requests.Add(source);
        RequestsReceived.Add(request);
        QueriesReceived.Add(request.Query);
        CursorsReceived.Add(cursor);

        var postings = _respond(request).Select(posting => posting with { Sweep = request.Sweep }).ToList();
        return Task.FromResult<IReadOnlyList<RawPosting>>(postings);
    }
}
