using Config;
using CvExtraction;
using JobPostings;
using Xunit;

namespace Host.Tests;

public class AreaResolverTests
{
    private static CvData CvIn(string? location) => new() { Name = "A", Location = location };

    [Theory]
    [InlineData("Torino", true, "Bologna", "Torino")]
    [InlineData("", true, "Bologna", "Bologna")]
    [InlineData("  ", true, " Bologna ", "Bologna")]
    [InlineData("", false, "Bologna", "")]
    [InlineData("", true, null, "")]
    [InlineData("", true, "  ", "")]
    public void Resolve_ConfiguredWhereWins_OtherwiseTheCvLocation(string where, bool fromCv, string? cvLocation, string expected)
    {
        var area = new AreaSettings { Country = "it", Where = where, WhereFromCv = fromCv, DistanceKm = 20 };

        var resolved = AreaResolver.Resolve(area, CvIn(cvLocation));

        Assert.Equal(expected, resolved.Area.Where);
        Assert.Equal(expected.Length == 0 ? null : 20, resolved.Area.DistanceKm);
    }

    [Theory]
    [InlineData("Bologna (BO)", "Bologna")]
    [InlineData("Via Roma 1, 40100 Bologna", "Bologna")]
    [InlineData("Bologna, Emilia-Romagna", "Bologna")]
    [InlineData("Bologna\n", "Bologna")]
    [InlineData("  Reggio   nell'Emilia ", "Reggio nell'Emilia")]
    [InlineData("50667 Köln, Deutschland", "Köln")]
    [InlineData("12345", "")]
    public void Resolve_CvLocation_IsReducedToAPlaceName(string cvLocation, string expected)
    {
        var resolved = AreaResolver.Resolve(new AreaSettings { Country = "it", WhereFromCv = true }, CvIn(cvLocation));

        Assert.Equal(expected, resolved.Area.Where);
        Assert.Equal(expected.Length > 0, resolved.FromCv);
    }

    [Fact]
    public async Task Runner_CvAreaWithNoLocalResults_Warns()
    {
        using var tmp = new TempDir();
        var source = new MockJobSource(_ => Array.Empty<RawPosting>());
        var runner = new JobbbyRunner(new DataDir(tmp.Root),
            new RunDependencies(source, new MockLlmNoop(), new[] { new SourceDefinition { Name = "Adzuna", BaseUrl = "https://api.adzuna.com" } }));
        var d = JobbbySettings.Default;
        var settings = d with { Area = d.Area with { Country = "it" }, Searches = d.Searches with { Queries = new() { "q" }, DeriveFromCv = false } };

        var summary = await runner.RunAsync(settings, new CvData { Name = "A", Location = "Atlantide" }, RunMode.Dry, new Progress<RunEvent>(), CancellationToken.None);

        Assert.Contains(summary.Warnings, w => w.Contains("Atlantide") && w.Contains("area.where"));
    }

    [Fact]
    public void Resolve_DistanceWithoutAnyArea_IsDroppedWithWarning()
    {
        var area = new AreaSettings { Country = "it", Where = "", WhereFromCv = true, DistanceKm = 30 };

        var resolved = AreaResolver.Resolve(area, CvIn(null));

        Assert.Null(resolved.Area.DistanceKm);
        Assert.Contains(resolved.Warnings, w => w.Contains("raggio"));
    }

    [Fact]
    public void Resolve_ReportsWhereTheAreaCameFrom()
    {
        var resolved = AreaResolver.Resolve(new AreaSettings { Country = "it", WhereFromCv = true }, CvIn("Bologna"));

        Assert.Contains(resolved.Notes, n => n.Contains("Bologna") && n.Contains("CV"));
    }

    [Fact]
    public void Validate_DistanceWithEmptyWhere_IsFineWhenTakenFromCv()
    {
        var d = JobbbySettings.Default;
        var fromCv = d with { Area = d.Area with { Country = "it", DistanceKm = 20, WhereFromCv = true } };
        var notFromCv = d with { Area = d.Area with { Country = "it", DistanceKm = 20, WhereFromCv = false } };

        Assert.DoesNotContain(SettingsValidator.Validate(fromCv, forRun: true), e => e.Field == "area.distanceKm");
        Assert.Contains(SettingsValidator.Validate(notFromCv, forRun: true), e => e.Field == "area.distanceKm");
    }

    [Fact]
    public void Default_TakesTheAreaFromTheCv()
    {
        Assert.True(JobbbySettings.Default.Area.WhereFromCv);
    }

    [Fact]
    public async Task Runner_SearchesTheCvLocation_AndStageOneTrustsIt()
    {
        using var tmp = new TempDir();
        var source = new MockJobSource(_ => Array.Empty<RawPosting>());
        var runner = new JobbbyRunner(new DataDir(tmp.Root),
            new RunDependencies(source, new MockLlmNoop(), new[] { new SourceDefinition { Name = "Adzuna", BaseUrl = "https://api.adzuna.com" } }));
        var d = JobbbySettings.Default;
        var settings = d with
        {
            Area = d.Area with { Country = "de", DistanceKm = 15 },
            Searches = d.Searches with { Queries = new() { "Pflegefachkraft" }, DeriveFromCv = false },
        };

        var summary = await runner.RunAsync(settings, new CvData { Name = "A", Location = "Köln" }, RunMode.Dry, new Progress<RunEvent>(), CancellationToken.None);

        Assert.Equal(new JobSearchRequest("Pflegefachkraft", "Köln", 15, SearchSweep.Local), Assert.Single(source.RequestsReceived));
        Assert.Equal("Köln", summary.Area.Where);
    }

    private sealed class MockLlmNoop : GraphEngine.ILlmClient
    {
        public Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken = default) => Task.FromResult("{}");
    }
}
