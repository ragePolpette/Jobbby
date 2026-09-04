using System.Globalization;
using System.Text.Json;
using Config;
using Reporting;

namespace JobPostings;

/// <summary>
/// Real <see cref="IJobSource"/> backed by the Adzuna Jobs API (Italy search endpoint).
/// </summary>
public sealed class AdzunaJobSource : IJobSource
{
    private const string SearchEndpoint = "https://api.adzuna.com/v1/api/jobs/it/search/1";

    private readonly HttpClient _httpClient;
    private readonly string _appId;
    private readonly string _appKey;
    private readonly int _resultsPerPage;

    /// <param name="resultsPerPage">
    /// Caps how many postings Adzuna returns per source per fetch. Kept deliberately
    /// small by default (10) so a first real run doesn't fan out into an uncontrolled
    /// number of GraphRuns - and Telegram approval requests - all at once.
    /// </param>
    public AdzunaJobSource(HttpClient httpClient, string appId, string appKey, int resultsPerPage = 10)
    {
        _httpClient = httpClient;
        _appId = appId;
        _appKey = appKey;
        _resultsPerPage = resultsPerPage;
    }

    /// <summary>
    /// Builds a source resolving app_id/app_key via <see cref="SourceWhitelist.ResolveSecret"/>
    /// - the same user-secrets/environment-variable resolution used everywhere else in
    /// this codebase. Never hardcoded.
    /// </summary>
    public static AdzunaJobSource FromEnvironment(
        HttpClient httpClient,
        string appIdSecretKey = "Adzuna:AppId",
        string appKeySecretKey = "Adzuna:AppKey",
        int resultsPerPage = 10)
    {
        var appId = SourceWhitelist.ResolveSecret(appIdSecretKey)
            ?? throw new InvalidOperationException(
                $"Missing secret '{appIdSecretKey}'. Set it with `dotnet user-secrets set {appIdSecretKey} <value>` " +
                "in development, or as an environment variable in production.");

        var appKey = SourceWhitelist.ResolveSecret(appKeySecretKey)
            ?? throw new InvalidOperationException(
                $"Missing secret '{appKeySecretKey}'. Set it with `dotnet user-secrets set {appKeySecretKey} <value>` " +
                "in development, or as an environment variable in production.");

        return new AdzunaJobSource(httpClient, appId, appKey, resultsPerPage);
    }

    public async Task<IReadOnlyList<RawPosting>> FetchAsync(
        SourceDefinition source, SourceCursor? cursor = null, CancellationToken cancellationToken = default)
    {
        // SourceDefinition has no dedicated search-query field, so a whitelist entry's
        // Name doubles as the Adzuna "what" keyword - configurable per source.
        // sort_by=date asks Adzuna itself to order results newest-first.
        var url = $"{SearchEndpoint}?app_id={Uri.EscapeDataString(_appId)}&app_key={Uri.EscapeDataString(_appKey)}" +
                  $"&what={Uri.EscapeDataString(source.Name)}&results_per_page={_resultsPerPage}&sort_by=date&content-type=application/json";

        if (cursor is not null)
        {
            // Adzuna's search endpoint filters by listing age via max_days_old (days
            // since posted), not by a since-this-id/timestamp cursor - so the previous
            // cursor's LastRunAt becomes "how many days ago was that", rounded up so a
            // sub-day gap between runs still asks for at least today's listings.
            var daysSinceLastRun = Math.Max(1, (int)Math.Ceiling((DateTimeOffset.UtcNow - cursor.LastRunAt).TotalDays));
            url += $"&max_days_old={daysSinceLastRun}";
        }

        using var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (!document.RootElement.TryGetProperty("results", out var results))
                return Array.Empty<RawPosting>();

            // "The source's domain" for later apply-channel decisions: the host of the
            // whitelisted source itself, not of any individual result.
            var sourceDomain = Uri.TryCreate(source.BaseUrl, UriKind.Absolute, out var sourceUri)
                ? sourceUri.Host
                : source.BaseUrl;

            var postings = new List<RawPosting>();
            foreach (var result in results.EnumerateArray())
            {
                var title = GetStringOrEmpty(result, "title");
                var description = GetStringOrEmpty(result, "description");
                var applyUrl = GetStringOrEmpty(result, "redirect_url");
                var company = GetCompanyDisplayName(result);
                var postedAt = GetCreatedAt(result);

                postings.Add(new RawPosting(title, description, applyUrl, sourceDomain, company, postedAt));
            }

            // Best-effort dedup aid alongside max_days_old: drop the one posting we know
            // for certain we already saw last time, in case of overlap at the day boundary.
            if (cursor is not null && !string.IsNullOrEmpty(cursor.LastSeenIdentifier))
                postings.RemoveAll(p => p.ApplyUrl == cursor.LastSeenIdentifier);

            return postings;
        }
    }

    private static string GetStringOrEmpty(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) ? value.GetString() ?? string.Empty : string.Empty;

    // Adzuna nests the employer name as company.display_name (see
    // https://developer.adzuna.com/docs/search - the "company" object on each result).
    private static string GetCompanyDisplayName(JsonElement result) =>
        result.TryGetProperty("company", out var company)
            ? GetStringOrEmpty(company, "display_name")
            : string.Empty;

    // Adzuna's "created" field is an ISO 8601 UTC timestamp, e.g. "2013-11-08T18:07:39Z"
    // (see https://developer.adzuna.com/docs/search), marking when the ad was posted.
    private static DateTimeOffset? GetCreatedAt(JsonElement result) =>
        result.TryGetProperty("created", out var created) &&
        DateTimeOffset.TryParse(created.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
}
