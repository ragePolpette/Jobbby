using Config;
using JobPostings;
using Reporting;

namespace Host;

public sealed record QueryFetchOutcome(string Query, int Returned, SourceCursor? Cursor, string? Error);

public sealed record SourceFetchResult(IReadOnlyList<RawPosting> Postings, IReadOnlyList<QueryFetchOutcome> Queries);

/// <summary>
/// Runs every search query against one source and merges the results. Each
/// (source, query) pair keeps its own cursor, and postings returned by more than one
/// query are kept once, so overlapping queries don't cause duplicate evaluations.
/// A failing query is reported in its outcome without affecting the others.
/// </summary>
public static class MultiQueryFetcher
{
    public static string CursorKey(SourceDefinition source, string query) => $"{source.Name}|{query}";

    public static async Task<SourceFetchResult> FetchAsync(
        IJobSource jobSource,
        SourceDefinition source,
        IReadOnlyList<string> queries,
        IReadOnlyDictionary<string, SourceCursor> cursors,
        int? postingLimitPerQuery,
        DateTimeOffset runAt,
        CancellationToken cancellationToken = default)
    {
        var merged = new List<RawPosting>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var outcomes = new List<QueryFetchOutcome>();

        foreach (var query in queries)
        {
            var key = CursorKey(source, query);
            cursors.TryGetValue(key, out var cursor);

            IReadOnlyList<RawPosting> fetched;
            try
            {
                fetched = await jobSource.FetchAsync(source, query, cursor, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                outcomes.Add(new QueryFetchOutcome(query, 0, cursor, ex.Message));
                continue;
            }

            var limited = postingLimitPerQuery is null ? fetched : fetched.Take(postingLimitPerQuery.Value).ToList();
            foreach (var posting in limited)
            {
                if (seen.Add(IdentityOf(posting)))
                    merged.Add(posting);
            }

            var nextCursor = limited.Count == 0 ? cursor : CursorSelection.SelectMostRecent(key, limited, runAt);
            outcomes.Add(new QueryFetchOutcome(query, fetched.Count, nextCursor, null));
        }

        return new SourceFetchResult(merged, outcomes);
    }

    private static string IdentityOf(RawPosting posting) =>
        string.IsNullOrWhiteSpace(posting.ApplyUrl)
            ? $"{posting.RawTitle}|{posting.Company}"
            : posting.ApplyUrl;
}
