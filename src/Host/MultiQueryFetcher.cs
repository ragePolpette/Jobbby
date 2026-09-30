using System.Globalization;
using System.Text.RegularExpressions;
using Config;
using JobPostings;
using Reporting;

namespace Host;

/// <param name="Returned">What the source returned for this sweep, before limit and prefilter.</param>
/// <param name="DroppedByPrefilter">Remote-sweep results discarded by the keyword prefilter.</param>
public sealed record QueryFetchOutcome(string Query, SearchSweep Sweep, int Returned, int DroppedByPrefilter, SourceCursor? Cursor, string? Error);

public sealed record SourceFetchResult(IReadOnlyList<RawPosting> Postings, IReadOnlyList<QueryFetchOutcome> Queries)
{
    /// <summary>Requests sent to the source, failed ones included: what counts against its quota.</summary>
    public int AdzunaCalls => Queries.Count;
}

/// <summary>
/// Runs every search query against one source and merges the results. Each query runs a
/// local sweep (restricted to the configured area) and, when remote jobs are accepted, an
/// area and keywords are configured, a remote sweep over the whole country whose results
/// must pass <see cref="RemoteKeywordFilter"/>. Every (source, country, area, sweep, query)
/// keeps its own cursor, and postings returned more than once are kept once. A failing
/// request is reported in its outcome without affecting the others.
/// </summary>
public static class MultiQueryFetcher
{
    public static string CursorKey(SourceDefinition source, string country, AreaSettings area, SearchSweep sweep, string query) =>
        string.Join('|',
            source.Name,
            country,
            area.Where.Trim(),
            area.DistanceKm?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            sweep == SearchSweep.Remote ? "remote" : "local",
            query);

    public static async Task<SourceFetchResult> FetchAsync(
        IJobSource jobSource,
        SourceDefinition source,
        IReadOnlyList<string> queries,
        string country,
        AreaSettings area,
        RemoteKeywordFilter remoteFilter,
        IReadOnlyDictionary<string, SourceCursor> cursors,
        int? postingLimitPerQuery,
        DateTimeOffset runAt,
        CancellationToken cancellationToken = default)
    {
        var merged = new List<RawPosting>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var outcomes = new List<QueryFetchOutcome>();
        var hasArea = !string.IsNullOrWhiteSpace(area.Where);
        var sweeps = area.AcceptsRemote && hasArea && !remoteFilter.IsEmpty
            ? new[] { SearchSweep.Local, SearchSweep.Remote }
            : new[] { SearchSweep.Local };

        // Every local sweep runs before any remote one, so a posting found by both is kept
        // with the local (geo-filtered) provenance and judged by the in-area rules.
        foreach (var sweep in sweeps)
        {
            foreach (var query in queries)
            {
                var key = CursorKey(source, country, area, sweep, query);
                cursors.TryGetValue(key, out var cursor);
                var request = sweep == SearchSweep.Local
                    ? new JobSearchRequest(query, hasArea ? area.Where.Trim() : null, hasArea ? area.DistanceKm : null, SearchSweep.Local)
                    : new JobSearchRequest(query, null, null, SearchSweep.Remote);

                IReadOnlyList<RawPosting> fetched;
                try
                {
                    fetched = await jobSource.FetchAsync(source, request, cursor, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    outcomes.Add(new QueryFetchOutcome(query, sweep, 0, 0, cursor, ex.Message));
                    continue;
                }

                var limited = postingLimitPerQuery is null ? fetched : fetched.Take(postingLimitPerQuery.Value).ToList();
                var dropped = 0;
                foreach (var posting in limited)
                {
                    if (sweep == SearchSweep.Remote && !remoteFilter.Matches(posting))
                    {
                        dropped++;
                        continue;
                    }

                    // Both keys are always recorded: the same ad under another URL, or a reposted
                    // ad (new id, "Acme S.r.l" vs "ACME SRL") under the same URL, is still one posting.
                    var urlSeen = !string.IsNullOrWhiteSpace(posting.ApplyUrl) && !seen.Add("url:" + posting.ApplyUrl);
                    var titleSeen = !seen.Add("job:" + TitleCompanyKey(posting));
                    if (!urlSeen && !titleSeen)
                        merged.Add(posting);
                }

                var nextCursor = limited.Count == 0 ? cursor : CursorSelection.SelectMostRecent(key, limited, runAt);
                outcomes.Add(new QueryFetchOutcome(query, sweep, fetched.Count, dropped, nextCursor, null));
            }
        }

        return new SourceFetchResult(merged, outcomes);
    }

    private static readonly Regex NonAlphanumeric = new(@"[^\p{L}\p{N}#+]+", RegexOptions.Compiled);

    private static readonly Regex LegalSuffix = new(
        @"\b(s ?r ?l ?s?|s ?p ?a|s ?a ?s|s ?n ?c|s ?a|gmbh|ag|kg|b ?v|n ?v|ltd|plc|llc|inc|corp|oy|ab|a ?s|s ?l|sp z ?o ?o|kft)$",
        RegexOptions.Compiled);

    internal static string TitleCompanyKey(RawPosting posting) =>
        $"{NormalizeText(posting.RawTitle)}|{NormalizeCompany(posting.Company)}";

    private static string NormalizeText(string value) =>
        NonAlphanumeric.Replace(value.ToLowerInvariant(), " ").Trim();

    private static string NormalizeCompany(string value) =>
        Regex.Replace(LegalSuffix.Replace(NormalizeText(value), " "), @"\s+", " ").Trim();
}
