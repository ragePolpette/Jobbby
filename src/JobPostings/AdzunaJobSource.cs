using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Config;
using GraphEngine;
using Microsoft.Extensions.Configuration;
using Reporting;

namespace JobPostings;

/// <summary>Timeouts and retries of <see cref="AdzunaJobSource"/>.</summary>
/// <param name="MaxAttempts">Requests per fetch, the first included.</param>
/// <param name="RequestTimeout">Per request, response body included. Default 20 s.</param>
/// <param name="BaseDelay">First backoff step, doubled at each retry (with jitter). Default 1 s.</param>
/// <param name="MaxDelay">Cap of a single backoff step. Default 20 s.</param>
/// <param name="MaxRetryAfter">A longer Retry-After from Adzuna fails the fetch instead of waiting. Default 60 s.</param>
public sealed record AdzunaRetryOptions(
    int MaxAttempts = 3,
    TimeSpan? RequestTimeout = null,
    TimeSpan? BaseDelay = null,
    TimeSpan? MaxDelay = null,
    TimeSpan? MaxRetryAfter = null,
    TimeProvider? TimeProvider = null)
{
    public static AdzunaRetryOptions Default { get; } = new();
}

/// <summary>
/// Real <see cref="IJobSource"/> backed by the Adzuna Jobs API, for one country.
/// Salary is never sent as a filter: it is filtered client-side in stage one, where
/// predicted and implausible values count as unknown. Each request has its own timeout;
/// 408, 429, 5xx, timeouts and network errors are retried with exponential backoff (honouring
/// Retry-After), and a fetch that still fails throws <see cref="JobSourceException"/>, whose
/// message never contains the URL: Adzuna takes the credentials in the query string.
/// </summary>
public sealed class AdzunaJobSource : IJobSource
{
    private static readonly Regex CountryCode = new("^[a-z]{2}$", RegexOptions.Compiled);

    private readonly HttpClient _httpClient;
    private readonly string _appId;
    private readonly string _appKey;
    private readonly int _resultsPerPage;
    private readonly string _searchEndpoint;
    private readonly decimal _minimumPlausibleSalary;
    private readonly int _maxAttempts;
    private readonly TimeSpan _requestTimeout;
    private readonly TimeSpan _baseDelay;
    private readonly TimeSpan _maxDelay;
    private readonly TimeSpan _maxRetryAfter;
    private readonly TimeProvider _timeProvider;

    /// <param name="resultsPerPage">
    /// Caps how many postings Adzuna returns per source per fetch. Kept deliberately
    /// small by default (10) so a first real run doesn't fan out into an uncontrolled
    /// number of GraphRuns all at once.
    /// </param>
    /// <param name="country">Adzuna country code, lower case (e.g. "gb"); part of the URL path.</param>
    /// <param name="minimumPlausibleSalary">Advertised salaries below this are not yearly figures and count as unknown.</param>
    public AdzunaJobSource(HttpClient httpClient, string appId, string appKey, string country, int resultsPerPage = 10, decimal minimumPlausibleSalary = 5000m,
        AdzunaRetryOptions? retry = null)
    {
        if (!CountryCode.IsMatch(country))
            throw new ArgumentException($"Invalid Adzuna country code '{country}'.", nameof(country));

        retry ??= AdzunaRetryOptions.Default;
        _searchEndpoint = $"https://api.adzuna.com/v1/api/jobs/{country}/search/1";
        _minimumPlausibleSalary = minimumPlausibleSalary;
        _httpClient = httpClient;
        _appId = appId;
        _appKey = appKey;
        _resultsPerPage = resultsPerPage;
        _maxAttempts = Math.Max(1, retry.MaxAttempts);
        _requestTimeout = retry.RequestTimeout ?? TimeSpan.FromSeconds(20);
        _baseDelay = retry.BaseDelay ?? TimeSpan.FromSeconds(1);
        _maxDelay = retry.MaxDelay ?? TimeSpan.FromSeconds(20);
        _maxRetryAfter = retry.MaxRetryAfter ?? TimeSpan.FromSeconds(60);
        _timeProvider = retry.TimeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Builds a source with app_id/app_key resolved via <see cref="SourceWhitelist.ResolveSecret"/>
    /// from the given configuration (user-secrets / environment). Never hardcoded.
    /// </summary>
    public static AdzunaJobSource FromConfiguration(
        HttpClient httpClient,
        IConfiguration? configuration,
        string country,
        int resultsPerPage = 10,
        decimal minimumPlausibleSalary = 5000m,
        string appIdSecretKey = "Adzuna:AppId",
        string appKeySecretKey = "Adzuna:AppKey",
        AdzunaRetryOptions? retry = null)
    {
        var appId = SourceWhitelist.ResolveSecret(configuration, appIdSecretKey)
            ?? throw new InvalidOperationException(
                $"Missing secret '{appIdSecretKey}'. Set it with `dotnet user-secrets set {appIdSecretKey} <value>` " +
                "in development, or as an environment variable in production.");

        var appKey = SourceWhitelist.ResolveSecret(configuration, appKeySecretKey)
            ?? throw new InvalidOperationException(
                $"Missing secret '{appKeySecretKey}'. Set it with `dotnet user-secrets set {appKeySecretKey} <value>` " +
                "in development, or as an environment variable in production.");

        return new AdzunaJobSource(httpClient, appId, appKey, country, resultsPerPage, minimumPlausibleSalary, retry);
    }

    public async Task<IReadOnlyList<RawPosting>> FetchAsync(
        SourceDefinition source, JobSearchRequest request, SourceCursor? cursor = null, CancellationToken cancellationToken = default)
    {
        // sort_by=date asks Adzuna itself to order results newest-first.
        var url = $"{_searchEndpoint}?app_id={Uri.EscapeDataString(_appId)}&app_key={Uri.EscapeDataString(_appKey)}" +
                  $"&what={Uri.EscapeDataString(request.Query)}&results_per_page={_resultsPerPage}&sort_by=date&content-type=application/json";

        // The area applies to the local sweep only; the remote sweep searches the whole country.
        if (request.Sweep == SearchSweep.Local && !string.IsNullOrWhiteSpace(request.Where))
        {
            url += $"&where={Uri.EscapeDataString(request.Where.Trim())}";
            if (request.DistanceKm is not null)
                url += $"&distance={request.DistanceKm.Value.ToString(CultureInfo.InvariantCulture)}";
        }

        if (cursor is not null)
        {
            // Adzuna's search endpoint filters by listing age via max_days_old (days
            // since posted), not by a since-this-id/timestamp cursor - so the previous
            // cursor's LastRunAt becomes "how many days ago was that", rounded up so a
            // sub-day gap between runs still asks for at least today's listings.
            var daysSinceLastRun = Math.Max(1, (int)Math.Ceiling((DateTimeOffset.UtcNow - cursor.LastRunAt).TotalDays));
            url += $"&max_days_old={daysSinceLastRun}";
        }

        using var response = await SendWithRetryAsync(url, cancellationToken).ConfigureAwait(false);

        try
        {
            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
            {
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
                return Parse(document, source, request, cursor);
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            throw new JobSourceException("Adzuna returned a response that is not valid search JSON.", response.StatusCode, innerException: ex);
        }
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(string url, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            TimeSpan? retryAfter = null;
            JobSourceException failure;
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(_requestTimeout);
                try
                {
                    var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseContentRead, timeout.Token).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                        return response;

                    retryAfter = RetryBackoff.RetryAfter(response, _timeProvider);
                    failure = new JobSourceException(
                        $"Adzuna responded with HTTP {(int)response.StatusCode}.", response.StatusCode, RetryBackoff.IsTransient(response.StatusCode));
                    response.Dispose();
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    failure = new JobSourceException(
                        $"Adzuna did not respond within {_requestTimeout.TotalSeconds:0.#} s.", HttpStatusCode.RequestTimeout, isTransient: true);
                }
                catch (HttpRequestException ex)
                {
                    failure = new JobSourceException($"Adzuna could not be reached: {SecretRedaction.Redact(ex.Message)}", ex.StatusCode, isTransient: true);
                }
            }

            if (!failure.IsTransient || attempt >= _maxAttempts || retryAfter > _maxRetryAfter)
                throw failure;

            await Task.Delay(RetryBackoff.Delay(attempt, _baseDelay, _maxDelay, retryAfter), _timeProvider, cancellationToken).ConfigureAwait(false);
        }
    }

    private List<RawPosting> Parse(JsonDocument document, SourceDefinition source, JobSearchRequest request, SourceCursor? cursor)
    {
        if (!document.RootElement.TryGetProperty("results", out var results))
            return new List<RawPosting>();

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
            var location = result.TryGetProperty("location", out var locationElement)
                ? NullIfEmpty(GetStringOrEmpty(locationElement, "display_name"))
                : null;

            postings.Add(new RawPosting(title, description, applyUrl, sourceDomain, company, postedAt, location, GetAdvertisedSalaryMaximum(result), request.Sweep,
                GetId(result)));
        }

        // Best-effort dedup aid alongside max_days_old: drop the one posting we know
        // for certain we already saw last time, in case of overlap at the day boundary.
        if (cursor is not null && !string.IsNullOrEmpty(cursor.LastSeenIdentifier))
            postings.RemoveAll(p => p.ApplyUrl == cursor.LastSeenIdentifier);

        return postings;
    }

    // Adzuna sends the ad id as a string ("4519856154"); a number is accepted as well.
    private static string? GetId(JsonElement result) =>
        result.TryGetProperty("id", out var id)
            ? id.ValueKind switch
            {
                JsonValueKind.String => NullIfEmpty(id.GetString() ?? string.Empty),
                JsonValueKind.Number => id.GetRawText(),
                _ => null,
            }
            : null;

    private static string GetStringOrEmpty(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    // salary_is_predicted = "1" marks Adzuna's own estimate rather than the advertiser's figure;
    // filtering candidates on a guess would reject postings for the wrong reason. Values below
    // the plausibility threshold (real responses carry e.g. 38 or 70: thousands, or hourly or
    // daily rates) are not yearly figures either.
    private decimal? GetAdvertisedSalaryMaximum(JsonElement result)
    {
        if (result.TryGetProperty("salary_is_predicted", out var predicted) && predicted.ToString() == "1")
            return null;

        if (!result.TryGetProperty("salary_max", out var salary) || salary.ValueKind != JsonValueKind.Number)
            return null;

        var value = salary.GetDecimal();
        return value >= _minimumPlausibleSalary ? value : null;
    }

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
