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

    public AdzunaJobSource(HttpClient httpClient, string appId, string appKey)
    {
        _httpClient = httpClient;
        _appId = appId;
        _appKey = appKey;
    }

    /// <summary>
    /// Builds a source resolving app_id/app_key the same way secrets are resolved
    /// elsewhere in this codebase: `dotnet user-secrets set &lt;key&gt; &lt;value&gt;` in
    /// development, or environment variables of the same name in production. Never
    /// hardcoded.
    /// </summary>
    public static AdzunaJobSource FromEnvironment(
        HttpClient httpClient,
        string appIdSecretKey = "Adzuna:AppId",
        string appKeySecretKey = "Adzuna:AppKey")
    {
        var appId = SourceWhitelist.ResolveSecret(appIdSecretKey)
            ?? throw new InvalidOperationException(
                $"Missing secret '{appIdSecretKey}'. Set it with `dotnet user-secrets set {appIdSecretKey} <value>` " +
                "in development, or as an environment variable in production.");

        var appKey = SourceWhitelist.ResolveSecret(appKeySecretKey)
            ?? throw new InvalidOperationException(
                $"Missing secret '{appKeySecretKey}'. Set it with `dotnet user-secrets set {appKeySecretKey} <value>` " +
                "in development, or as an environment variable in production.");

        return new AdzunaJobSource(httpClient, appId, appKey);
    }

    public async Task<IReadOnlyList<RawPosting>> FetchAsync(SourceDefinition source, CancellationToken cancellationToken = default)
    {
        // SourceDefinition has no dedicated search-query field, so a whitelist entry's
        // Name doubles as the Adzuna "what" keyword - configurable per source.
        var url = $"{SearchEndpoint}?app_id={Uri.EscapeDataString(_appId)}&app_key={Uri.EscapeDataString(_appKey)}" +
                  $"&what={Uri.EscapeDataString(source.Name)}&content-type=application/json";

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

                postings.Add(new RawPosting(title, description, applyUrl, sourceDomain));
            }

            return postings;
        }
    }

    private static string GetStringOrEmpty(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) ? value.GetString() ?? string.Empty : string.Empty;
}
