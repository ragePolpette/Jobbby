using Config;
using JobPostings;
using Reporting;
using Xunit;

namespace Host.Tests;

public class MultiQueryFetcherTests
{
    private static readonly SourceDefinition Adzuna = new() { Name = "Adzuna", BaseUrl = "https://api.adzuna.com", Type = "api" };
    private static readonly DateTimeOffset RunAt = new(2026, 9, 29, 8, 0, 0, TimeSpan.Zero);
    private static readonly AreaSettings NoArea = new() { Country = "it" };
    private static readonly RemoteKeywordFilter NoKeywords = new(Array.Empty<string>());

    [Fact]
    public async Task FetchAsync_RunsEveryQueryAndDeduplicatesByApplyUrl()
    {
        var shared = Posting("Senior .NET", "https://jobs.example/1");
        var jobSource = new MockJobSource(new Dictionary<string, IReadOnlyList<RawPosting>>
        {
            [".NET developer"] = new[] { shared, Posting("Backend C#", "https://jobs.example/2") },
            ["AI engineer"] = new[] { shared, Posting("AI Engineer", "https://jobs.example/3") },
        });

        var result = await Fetch(jobSource, new[] { ".NET developer", "AI engineer" });

        Assert.Equal(new[] { ".NET developer", "AI engineer" }, jobSource.QueriesReceived);
        Assert.Equal(3, result.Postings.Count);
        Assert.Equal(new[] { 2, 2 }, result.Queries.Select(q => q.Returned));
    }

    [Fact]
    public async Task FetchAsync_RepostedAdWithNewUrlAndCompanySpelling_IsKeptOnce()
    {
        var jobSource = new MockJobSource(new Dictionary<string, IReadOnlyList<RawPosting>>
        {
            [".NET developer"] = new[]
            {
                Posting("Senior Developer .NET - Solution Architect", "https://jobs.example/5902639270", "JUMPIT S.R.L."),
                Posting("Senior Developer .NET - Solution Architect", "https://jobs.example/5902278516", "Jumpit S.r.l"),
                Posting("Microsoft Full Stack Developer (C#  SQL)", "https://jobs.example/1", "NTT America, Inc."),
                Posting("Microsoft Full Stack Developer (C# SQL)", "https://jobs.example/2", "NTT America Inc"),
                Posting("Microsoft Full Stack Developer (C# SQL)", "https://jobs.example/3", "NTT Data Italia"),
            },
        });

        var result = await Fetch(jobSource, new[] { ".NET developer" });

        Assert.Equal(
            new[] { "https://jobs.example/5902639270", "https://jobs.example/1", "https://jobs.example/3" },
            result.Postings.Select(p => p.ApplyUrl));
    }

    [Fact]
    public async Task FetchAsync_UsesAndProducesOneCursorPerQuery()
    {
        var previousKey = MultiQueryFetcher.CursorKey(Adzuna, "it", NoArea, SearchSweep.Local, "AI engineer");
        var previous = new SourceCursor(previousKey, "https://jobs.example/old", RunAt.AddDays(-2));
        var jobSource = new MockJobSource(new Dictionary<string, IReadOnlyList<RawPosting>>
        {
            [".NET developer"] = new[] { Posting("Senior .NET", "https://jobs.example/1") },
            ["AI engineer"] = Array.Empty<RawPosting>(),
        });

        var result = await Fetch(jobSource, new[] { ".NET developer", "AI engineer" },
            cursors: new Dictionary<string, SourceCursor> { [previousKey] = previous });

        Assert.Equal(new SourceCursor?[] { null, previous }, jobSource.CursorsReceived);
        var dotnet = result.Queries.Single(q => q.Query == ".NET developer");
        Assert.Equal("Adzuna|it|||local|.NET developer", dotnet.Cursor!.SourceName);
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

        var result = await Fetch(jobSource, new[] { "broken", "AI engineer" });

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

        var result = await Fetch(jobSource, new[] { "a", "b" }, limit: 1);

        Assert.Equal(new[] { "A1", "B1" }, result.Postings.Select(p => p.RawTitle));
    }

    [Fact]
    public async Task FetchAsync_PassesAreaToLocalSweep()
    {
        var jobSource = new MockJobSource(_ => Array.Empty<RawPosting>());
        var area = new AreaSettings { Country = "de", Where = "Köln", DistanceKm = 20 };

        await Fetch(jobSource, new[] { "q" }, area: area);

        Assert.Equal(new JobSearchRequest("q", "Köln", 20, SearchSweep.Local), Assert.Single(jobSource.RequestsReceived));
    }

    [Fact]
    public async Task FetchAsync_AcceptsRemoteWithWhere_RunsSecondSweep_PrefiltersAndDeduplicates()
    {
        var shared = Posting("Remote nurse", "https://x/1");
        var jobSource = new MockJobSource(request => request.Sweep == SearchSweep.Local
            ? new[] { shared }
            : new[] { shared, Posting("Remote nurse 2", "https://x/2"), Posting("On site", "https://x/3") });
        var area = new AreaSettings { Country = "de", Where = "Köln", DistanceKm = 20, AcceptsRemote = true };

        var result = await Fetch(jobSource, new[] { "nurse" }, area: area, filter: new RemoteKeywordFilter(new[] { "remote" }));

        Assert.Equal(new[] { SearchSweep.Local, SearchSweep.Remote }, jobSource.RequestsReceived.Select(r => r.Sweep));
        Assert.Null(jobSource.RequestsReceived[1].Where);
        Assert.Equal(new[] { "https://x/1", "https://x/2" }, result.Postings.Select(p => p.ApplyUrl));
        Assert.Equal(SearchSweep.Remote, result.Postings[1].Sweep);
        var remote = result.Queries.Single(q => q.Sweep == SearchSweep.Remote);
        Assert.Equal(3, remote.Returned);
        Assert.Equal(1, remote.DroppedByPrefilter);
        Assert.Equal(2, result.AdzunaCalls);
    }

    [Fact]
    public async Task FetchAsync_PostingFromRemoteSweepOfOneQueryAndLocalOfAnother_IsKeptAsLocal()
    {
        // Regression: query 1's remote sweep ran before query 2's local sweep, so an in-area
        // hybrid job was kept as Remote and then rejected as "fuori zona".
        var inArea = Posting("Hybrid nurse, remote days", "https://x/1");
        var jobSource = new MockJobSource(request => (request.Query, request.Sweep) switch
        {
            ("q1", SearchSweep.Remote) => new[] { inArea },
            ("q2", SearchSweep.Local) => new[] { inArea },
            _ => Array.Empty<RawPosting>(),
        });
        var area = new AreaSettings { Country = "de", Where = "Köln", AcceptsRemote = true };

        var result = await Fetch(jobSource, new[] { "q1", "q2" }, area: area, filter: new RemoteKeywordFilter(new[] { "remote" }));

        Assert.Equal(SearchSweep.Local, Assert.Single(result.Postings).Sweep);
        Assert.Equal(
            new[] { SearchSweep.Local, SearchSweep.Local, SearchSweep.Remote, SearchSweep.Remote },
            jobSource.RequestsReceived.Select(r => r.Sweep));
    }

    [Fact]
    public async Task FetchAsync_RemoteSweep_SkippedWhenNoKeywordsOrNoWhereOrNotAccepted()
    {
        foreach (var (area, keywords) in new[]
                 {
                     (new AreaSettings { Country = "de", Where = "Köln", AcceptsRemote = true }, Array.Empty<string>()),
                     (new AreaSettings { Country = "de", Where = "", AcceptsRemote = true }, new[] { "remote" }),
                     (new AreaSettings { Country = "de", Where = "Köln", AcceptsRemote = false }, new[] { "remote" }),
                 })
        {
            var jobSource = new MockJobSource(_ => Array.Empty<RawPosting>());

            var result = await Fetch(jobSource, new[] { "q" }, area: area, filter: new RemoteKeywordFilter(keywords));

            Assert.All(jobSource.RequestsReceived, r => Assert.Equal(SearchSweep.Local, r.Sweep));
            Assert.Equal(1, result.AdzunaCalls);
        }
    }

    [Fact]
    public async Task FetchAsync_RemoteSweep_HasItsOwnCursor()
    {
        var jobSource = new MockJobSource(_ => new[] { Posting("Remote nurse", "https://x/1") });
        var area = new AreaSettings { Country = "de", Where = "Köln", DistanceKm = 20, AcceptsRemote = true };

        var result = await Fetch(jobSource, new[] { "nurse" }, area: area, filter: new RemoteKeywordFilter(new[] { "remote" }));

        Assert.Equal(
            new[] { "Adzuna|de|Köln|20|local|nurse", "Adzuna|de|Köln|20|remote|nurse" },
            result.Queries.Select(q => q.Cursor!.SourceName));
    }

    [Fact]
    public void CursorKey_ChangesWithAreaAndSweep()
    {
        var milano = new AreaSettings { Where = "Milano", DistanceKm = 30 };
        var a = MultiQueryFetcher.CursorKey(Adzuna, "it", milano, SearchSweep.Local, "q");
        var b = MultiQueryFetcher.CursorKey(Adzuna, "it", milano with { Where = "Torino" }, SearchSweep.Local, "q");
        var c = MultiQueryFetcher.CursorKey(Adzuna, "it", milano, SearchSweep.Remote, "q");
        var d = MultiQueryFetcher.CursorKey(Adzuna, "it", milano with { DistanceKm = 10 }, SearchSweep.Local, "q");
        var e = MultiQueryFetcher.CursorKey(Adzuna, "fr", milano, SearchSweep.Local, "q");

        Assert.Equal("Adzuna|it|Milano|30|local|q", a);
        Assert.Equal(5, new[] { a, b, c, d, e }.Distinct().Count());
    }

    private static Task<SourceFetchResult> Fetch(
        IJobSource jobSource,
        IReadOnlyList<string> queries,
        IReadOnlyDictionary<string, SourceCursor>? cursors = null,
        int? limit = null,
        AreaSettings? area = null,
        RemoteKeywordFilter? filter = null)
    {
        area ??= NoArea;
        return MultiQueryFetcher.FetchAsync(jobSource, Adzuna, queries, area.Country ?? "it", area, filter ?? NoKeywords,
            cursors ?? new Dictionary<string, SourceCursor>(), limit, RunAt);
    }

    private static RawPosting Posting(string title, string applyUrl, string company = "Acme") =>
        new(title, "description", applyUrl, "api.adzuna.com", company, RunAt.AddHours(-1));
}
