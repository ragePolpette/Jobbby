using Config;
using Reporting;

namespace JobPostings;

/// <summary>
/// Fetches raw postings for one <see cref="JobSearchRequest"/> (query, area, sweep) from one whitelisted source. Mirrors how <c>ILlmClient</c> decouples
/// callers from a specific provider - see
/// <see cref="AdzunaJobSource"/> for the real implementation and
/// <see cref="MockJobSource"/> for tests. <paramref name="cursor"/> is that source's
/// bookmark from a previous run (null on a source's first fetch), letting an
/// implementation ask its API for only what's new instead of refetching everything.
/// Failures are reported as <see cref="JobSourceException"/>, whose message is safe to show
/// and store (no credentials).
/// </summary>
public interface IJobSource
{
    Task<IReadOnlyList<RawPosting>> FetchAsync(SourceDefinition source, JobSearchRequest request, SourceCursor? cursor = null, CancellationToken cancellationToken = default);
}
