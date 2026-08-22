using System.Text.Json;
using Config;

namespace Discovery;

/// <summary>
/// Real <see cref="IWebSearchClient"/> backed by the Brave Search API.
/// </summary>
public sealed class BraveSearchClient : IWebSearchClient
{
    private const string SearchEndpoint = "https://api.search.brave.com/res/v1/web/search";

    private readonly HttpClient _httpClient;
    private readonly string _apiKey;

    public BraveSearchClient(HttpClient httpClient, string apiKey)
    {
        _httpClient = httpClient;
        _apiKey = apiKey;
    }

    /// <summary>
    /// Builds a client resolving the API key via <see cref="SourceWhitelist.ResolveSecret"/>
    /// - the same user-secrets/environment-variable resolution used everywhere else in
    /// this codebase. Never hardcoded.
    /// </summary>
    public static BraveSearchClient FromEnvironment(HttpClient httpClient, string apiKeySecretKey = "BraveSearch:ApiKey")
    {
        var apiKey = SourceWhitelist.ResolveSecret(apiKeySecretKey)
            ?? throw new InvalidOperationException(
                $"Missing secret '{apiKeySecretKey}'. Set it with `dotnet user-secrets set {apiKeySecretKey} <value>` " +
                "in development, or as an environment variable in production.");

        return new BraveSearchClient(httpClient, apiKey);
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        var url = $"{SearchEndpoint}?q={Uri.EscapeDataString(query)}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Accept", "application/json");
        request.Headers.Add("X-Subscription-Token", _apiKey);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (!document.RootElement.TryGetProperty("web", out var web) ||
                !web.TryGetProperty("results", out var results))
            {
                return Array.Empty<SearchResult>();
            }

            var searchResults = new List<SearchResult>();
            foreach (var result in results.EnumerateArray())
            {
                var title = GetStringOrEmpty(result, "title");
                var resultUrl = GetStringOrEmpty(result, "url");
                var snippet = GetStringOrEmpty(result, "description");

                searchResults.Add(new SearchResult(title, resultUrl, snippet));
            }

            return searchResults;
        }
    }

    private static string GetStringOrEmpty(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) ? value.GetString() ?? string.Empty : string.Empty;
}
