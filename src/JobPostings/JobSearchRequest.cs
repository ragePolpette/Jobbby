namespace JobPostings;

/// <summary>
/// Which search produced a posting: the <see cref="Local"/> one (restricted to the configured
/// area when there is one) or the <see cref="Remote"/> sweep (no area, kept only for remote jobs).
/// </summary>
public enum SearchSweep
{
    Local,
    Remote,
}

/// <summary>
/// One search against a source. <see cref="Where"/>/<see cref="DistanceKm"/> are applied only
/// to the <see cref="SearchSweep.Local"/> sweep; the remote sweep searches the whole country.
/// </summary>
public sealed record JobSearchRequest(string Query, string? Where, int? DistanceKm, SearchSweep Sweep);
