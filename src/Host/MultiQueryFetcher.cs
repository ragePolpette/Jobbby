using System.Text.RegularExpressions;
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
                fetched = await jobSource.FetchAsync(source, new JobSearchRequest(query, null, null, SearchSweep.Local), cursor, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                outcomes.Add(new QueryFetchOutcome(query, 0, cursor, ex.Message));
                continue;
            }

            var limited = postingLimitPerQuery is null ? fetched : fetched.Take(postingLimitPerQuery.Value).ToList();
            foreach (var posting in limited)
            {
                // Both keys are always recorded: the same ad under another URL, or a reposted
                // ad (new id, "Acme S.r.l" vs "ACME SRL") under the same URL, is still one posting.
                var urlSeen = !string.IsNullOrWhiteSpace(posting.ApplyUrl) && !seen.Add("url:" + posting.ApplyUrl);
                var titleSeen = !seen.Add("job:" + TitleCompanyKey(posting));
                if (!urlSeen && !titleSeen)
                    merged.Add(posting);
            }

            var nextCursor = limited.Count == 0 ? cursor : CursorSelection.SelectMostRecent(key, limited, runAt);
            outcomes.Add(new QueryFetchOutcome(query, fetched.Count, nextCursor, null));
        }

        return new SourceFetchResult(merged, outcomes);
    }

    private static readonly Regex NonAlphanumeric = new(@"[^\p{L}\p{N}#+]+", RegexOptions.Compiled);

    private static readonly Regex LegalSuffix = new(
        @"\b(s ?r ?l ?s?|s ?p ?a|s ?a ?s|s ?n ?c|inc|ltd|llc|gmbh|ag|bv|plc|corp|co|company|group|italia)\b",
        RegexOptions.Compiled);

    internal static string TitleCompanyKey(RawPosting posting) =>
        $"{NormalizeText(posting.RawTitle)}|{NormalizeCompany(posting.Company)}";

    private static string NormalizeText(string value) =>
        NonAlphanumeric.Replace(value.ToLowerInvariant(), " ").Trim();

    private static string NormalizeCompany(string value) =>
        Regex.Replace(LegalSuffix.Replace(NormalizeText(value), " "), @"\s+", " ").Trim();
}
