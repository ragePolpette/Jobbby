using Config;
using Reporting;

namespace JobPostings;

/// <summary>
/// Fetches raw postings from one whitelisted source. Mirrors how <c>ILlmClient</c> and
/// <c>IWebSearchClient</c> decouple callers from a specific provider - see
/// <see cref="AdzunaJobSource"/> for the real implementation and
/// <see cref="MockJobSource"/> for tests. <paramref name="cursor"/> is that source's
/// bookmark from a previous run (null on a source's first fetch), letting an
/// implementation ask its API for only what's new instead of refetching everything.
/// </summary>
public interface IJobSource
{
    Task<IReadOnlyList<RawPosting>> FetchAsync(SourceDefinition source, SourceCursor? cursor = null, CancellationToken cancellationToken = default);
}
