using Config;

namespace JobPostings;

/// <summary>
/// Fetches raw postings from one whitelisted source. Mirrors how <c>ILlmClient</c> and
/// <c>IWebSearchClient</c> decouple callers from a specific provider - see
/// <see cref="AdzunaJobSource"/> for the real implementation and
/// <see cref="MockJobSource"/> for tests.
/// </summary>
public interface IJobSource
{
    Task<IReadOnlyList<RawPosting>> FetchAsync(SourceDefinition source, CancellationToken cancellationToken = default);
}
