using System.Text.Json;
using Config;

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

    public async Task<IReadOnlyList<RawPosting>> FetchAsync(SourceDefinition source, CancellationToken cancellationToken = default)
    {
        // SourceDefinition has no dedicated search-query field, so a whitelist entry's
        // Name doubles as the Adzuna "what" keyword - configurable per source.
        var url = $"{SearchEndpoint}?app_id={Uri.EscapeDataString(_appId)}&app_key={Uri.EscapeDataString(_appKey)}" +
                  $"&what={Uri.EscapeDataString(source.Name)}&results_per_page={_resultsPerPage}&content-type=application/json";

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

                postings.Add(new RawPosting(title, description, applyUrl, sourceDomain, company));
            }

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
}
