using System.Net;
using System.Text;
using Config;
using Xunit;

namespace JobPostings.Tests;

public class AdzunaJobSourceTests
{
    private const string SampleResponse = """
        {
          "results": [
            {
              "title": "Backend Engineer",
              "description": "Sviluppo di API in C# e .NET",
              "redirect_url": "https://www.adzuna.it/land/ad/12345",
              "company": { "display_name": "Acme Corp" }
            },
            {
              "title": "Frontend Developer",
              "description": "React e TypeScript",
              "redirect_url": "mailto:hr@example.com"
            }
          ]
        }
        """;

    [Fact]
    public async Task FetchAsync_MapsAdzunaResultsIntoRawPostings()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(SampleResponse, Encoding.UTF8, "application/json"),
        });

        using var httpClient = new HttpClient(handler);
        var jobSource = new AdzunaJobSource(httpClient, "test-id", "test-key");
        var source = new SourceDefinition
        {
            Name = "sviluppatore backend",
            BaseUrl = "https://api.adzuna.com",
            Type = "api",
            RequiresAuth = true,
            AuthSecretKey = "Adzuna:AppKey",
        };

        var postings = await jobSource.FetchAsync(source);

        Assert.Equal(2, postings.Count);

        Assert.Equal("Backend Engineer", postings[0].RawTitle);
        Assert.Equal("Sviluppo di API in C# e .NET", postings[0].RawDescription);
        Assert.Equal("https://www.adzuna.it/land/ad/12345", postings[0].ApplyUrl);
        Assert.Equal("api.adzuna.com", postings[0].SourceDomain); // host of the source's own BaseUrl
        Assert.Equal("Acme Corp", postings[0].Company); // company.display_name

        Assert.Equal("Frontend Developer", postings[1].RawTitle);
        Assert.Equal("mailto:hr@example.com", postings[1].ApplyUrl);
        Assert.Equal(string.Empty, postings[1].Company); // no "company" object in this result
    }

    [Fact]
    public async Task FetchAsync_SendsAppCredentialsAndSourceNameAsWhatQuery()
    {
        HttpRequestMessage? capturedRequest = null;

        var handler = new StubHttpMessageHandler(request =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"results":[]}""", Encoding.UTF8, "application/json"),
            };
        });

        using var httpClient = new HttpClient(handler);
        var jobSource = new AdzunaJobSource(httpClient, "my-app-id", "my-app-key");
        var source = new SourceDefinition
        {
            Name = "sviluppatore backend",
            BaseUrl = "https://api.adzuna.com",
            Type = "api",
            RequiresAuth = true,
            AuthSecretKey = "Adzuna:AppKey",
        };

        await jobSource.FetchAsync(source);

        Assert.NotNull(capturedRequest);
        var query = capturedRequest!.RequestUri!.Query;
        Assert.Contains("app_id=my-app-id", query);
        Assert.Contains("app_key=my-app-key", query);
        Assert.Contains("what=sviluppatore", query); // Uri-escaped source.Name
        Assert.Contains("results_per_page=10", query); // default cap
        Assert.StartsWith("https://api.adzuna.com/v1/api/jobs/it/search/1", capturedRequest.RequestUri!.ToString());
    }

    [Fact]
    public async Task FetchAsync_CustomResultsPerPage_IsSentInQuery()
    {
        HttpRequestMessage? capturedRequest = null;

        var handler = new StubHttpMessageHandler(request =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"results":[]}""", Encoding.UTF8, "application/json"),
            };
        });

        using var httpClient = new HttpClient(handler);
        var jobSource = new AdzunaJobSource(httpClient, "id", "key", resultsPerPage: 3);
        var source = new SourceDefinition { Name = "x", BaseUrl = "https://api.adzuna.com", Type = "api", RequiresAuth = false, AuthSecretKey = null };

        await jobSource.FetchAsync(source);

        Assert.Contains("results_per_page=3", capturedRequest!.RequestUri!.Query);
    }

    [Fact]
    public async Task FetchAsync_NoResultsProperty_ReturnsEmptyList()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{}""", Encoding.UTF8, "application/json"),
        });

        using var httpClient = new HttpClient(handler);
        var jobSource = new AdzunaJobSource(httpClient, "id", "key");
        var source = new SourceDefinition { Name = "x", BaseUrl = "https://api.adzuna.com", Type = "api", RequiresAuth = false, AuthSecretKey = null };

        var postings = await jobSource.FetchAsync(source);

        Assert.Empty(postings);
    }
}
