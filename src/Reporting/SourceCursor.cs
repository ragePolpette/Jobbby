namespace Reporting;

/// <summary>
/// Per-source incremental-fetch bookmark: how far a job source got last time, so the
/// next run can ask for only what's newer instead of refetching everything.
/// LastSeenIdentifier is a source-specific identifier (e.g. an ApplyUrl) for the most
/// recent posting observed, kept as a best-effort dedup aid - the real filtering is by
/// LastRunAt (e.g. via a source's own "max age" query parameter).
/// </summary>
public sealed record SourceCursor(
    string SourceName,
    string LastSeenIdentifier,
    DateTimeOffset LastRunAt);
