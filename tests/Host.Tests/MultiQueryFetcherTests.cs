using Config;
using JobPostings;
using Reporting;
using Xunit;

namespace Host.Tests;

public class MultiQueryFetcherTests
{
    private static readonly SourceDefinition Adzuna = new() { Name = "Adzuna", BaseUrl = "https://api.adzuna.com", Type = "api" };
    private static readonly DateTimeOffset RunAt = new(2026, 9, 29, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task FetchAsync_RunsEveryQueryAndDeduplicatesByApplyUrl()
    {
        var shared = Posting("Senior .NET", "https://jobs.example/1");
        var jobSource = new MockJobSource(new Dictionary<string, IReadOnlyList<RawPosting>>
        {
            [".NET developer"] = new[] { shared, Posting("Backend C#", "https://jobs.example/2") },
            ["AI engineer"] = new[] { shared, Posting("AI Engineer", "https://jobs.example/3") },
        });

        var result = await MultiQueryFetcher.FetchAsync(jobSource, Adzuna, new[] { ".NET developer", "AI engineer" },
            new Dictionary<string, SourceCursor>(), postingLimitPerQuery: null, RunAt);

        Assert.Equal(new[] { ".NET developer", "AI engineer" }, jobSource.QueriesReceived);
        Assert.Equal(3, result.Postings.Count);
        Assert.Equal(new[] { 2, 2 }, result.Queries.Select(q => q.Returned));
    }

    [Fact]
    public async Task FetchAsync_UsesAndProducesOneCursorPerQuery()
    {
        var previous = new SourceCursor("Adzuna|AI engineer", "https://jobs.example/old", RunAt.AddDays(-2));
        var jobSource = new MockJobSource(new Dictionary<string, IReadOnlyList<RawPosting>>
        {
            [".NET developer"] = new[] { Posting("Senior .NET", "https://jobs.example/1") },
            ["AI engineer"] = Array.Empty<RawPosting>(),
        });

        var result = await MultiQueryFetcher.FetchAsync(jobSource, Adzuna, new[] { ".NET developer", "AI engineer" },
            new Dictionary<string, SourceCursor> { [previous.SourceName] = previous }, postingLimitPerQuery: null, RunAt);

        Assert.Equal(new SourceCursor?[] { null, previous }, jobSource.CursorsReceived);
        var dotnet = result.Queries.Single(q => q.Query == ".NET developer");
        Assert.Equal("Adzuna|.NET developer", dotnet.Cursor!.SourceName);
        Assert.Equal("https://jobs.example/1", dotnet.Cursor.LastSeenIdentifier);
        Assert.Same(previous, result.Queries.Single(q => q.Query == "AI engineer").Cursor);
    }

    [Fact]
    public async Task FetchAsync_FailingQuery_IsReportedWithoutStoppingTheOthers()
    {
        var jobSource = new MockJobSource(new Dictionary<string, IReadOnlyList<RawPosting>>
        {
            ["AI engineer"] = new[] { Posting("AI Engineer", "https://jobs.example/3") },
        });

        var result = await MultiQueryFetcher.FetchAsync(jobSource, Adzuna, new[] { "broken", "AI engineer" },
            new Dictionary<string, SourceCursor>(), postingLimitPerQuery: null, RunAt);

        Assert.Single(result.Postings);
        var failed = result.Queries.Single(q => q.Query == "broken");
        Assert.Contains("Simulated fetch failure", failed.Error);
        Assert.Null(failed.Cursor);
    }

    [Fact]
    public async Task FetchAsync_LimitAppliesPerQuery()
    {
        var jobSource = new MockJobSource(new Dictionary<string, IReadOnlyList<RawPosting>>
        {
            ["a"] = new[] { Posting("A1", "https://jobs.example/a1"), Posting("A2", "https://jobs.example/a2") },
            ["b"] = new[] { Posting("B1", "https://jobs.example/b1"), Posting("B2", "https://jobs.example/b2") },
        });

        var result = await MultiQueryFetcher.FetchAsync(jobSource, Adzuna, new[] { "a", "b" },
            new Dictionary<string, SourceCursor>(), postingLimitPerQuery: 1, RunAt);

        Assert.Equal(new[] { "A1", "B1" }, result.Postings.Select(p => p.RawTitle));
    }

    private static RawPosting Posting(string title, string applyUrl) =>
        new(title, "description", applyUrl, "api.adzuna.com", "Acme", RunAt.AddHours(-1));
}
