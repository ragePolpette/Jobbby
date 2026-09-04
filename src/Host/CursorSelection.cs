using JobPostings;
using Reporting;

namespace Host;

/// <summary>
/// Picks the actually most recent posting out of one fetch (by RawPosting.PostedAt) to
/// build the next SourceCursor - not just the first result in response order, since a
/// source isn't guaranteed to return results in strict chronological order even when
/// asked to (e.g. Adzuna's sort_by=date).
/// </summary>
public static class CursorSelection
{
    public static SourceCursor SelectMostRecent(string sourceName, IReadOnlyList<RawPosting> postings, DateTimeOffset runAt)
    {
        var mostRecent = postings.OrderByDescending(p => p.PostedAt ?? DateTimeOffset.MinValue).First();
        return new SourceCursor(sourceName, mostRecent.ApplyUrl, runAt);
    }
}
