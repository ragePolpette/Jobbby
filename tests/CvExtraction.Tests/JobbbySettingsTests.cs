using Config;
using Xunit;

namespace CvExtraction.Tests;

public class JobbbySettingsTests
{
    [Fact]
    public void Default_IsNeutral()
    {
        var s = JobbbySettings.Default;

        Assert.Empty(s.Searches.Queries);
        Assert.True(s.Searches.DeriveFromCv);
        Assert.Null(s.Area.Country);
        Assert.Equal("", s.Area.Where);
        Assert.Null(s.Area.DistanceKm);
        Assert.False(s.Area.AcceptsRemote);
        Assert.Null(s.Salary.MinimumYearly);
        Assert.Equal(5000m, s.Salary.MinimumPlausible);
        Assert.Equal(0.7, s.Evaluation.AutoApproveThreshold);
        Assert.Empty(s.RemoteSweep.Keywords);
        Assert.Empty(s.Dedupe.ExtraCompanySuffixes);
        Assert.Equal("", s.Presentation.Opening);
        Assert.Equal("", s.Presentation.Closing);
    }

    [Fact]
    public void Validate_ForRun_RequiresCountry_OnlyWhenRunning()
    {
        Assert.Contains(SettingsValidator.Validate(JobbbySettings.Default, forRun: true), e => e.Field == "area.country");
        Assert.DoesNotContain(SettingsValidator.Validate(JobbbySettings.Default, forRun: false), e => e.Field == "area.country");
    }

    [Theory]
    [InlineData("xx")]
    [InlineData("IT ")]
    [InlineData("")]
    public void Validate_RejectsUnsupportedCountry(string country)
    {
        var d = JobbbySettings.Default;
        var s = d with { Area = d.Area with { Country = country } };

        Assert.Contains(SettingsValidator.Validate(s, forRun: false), e => e.Field == "area.country");
    }

    [Fact]
    public void Validate_ReportsEveryBadFieldByName()
    {
        var d = JobbbySettings.Default;
        var s = d with
        {
            Area = d.Area with { Country = "de", Where = "Köln", DistanceKm = -1 },
            Evaluation = d.Evaluation with { AutoApproveThreshold = 1.5 },
            Searches = d.Searches with { DeriveFromCv = false, MaxDerivedQueries = 11 },
            Salary = d.Salary with { MinimumYearly = -3, MinimumPlausible = -1 },
            DryRun = d.DryRun with { MaxPostingsPerQuery = 0 },
            Llm = d.Llm with { Provider = "gemini" },
        };

        var fields = SettingsValidator.Validate(s, forRun: true).Select(e => e.Field).ToList();

        Assert.Contains("area.distanceKm", fields);
        Assert.Contains("evaluation.autoApproveThreshold", fields);
        Assert.Contains("searches.queries", fields);
        Assert.Contains("searches.maxDerivedQueries", fields);
        Assert.Contains("salary.minimumYearly", fields);
        Assert.Contains("salary.minimumPlausible", fields);
        Assert.Contains("dryRun.maxPostingsPerQuery", fields);
        Assert.Contains("llm.provider", fields);
        Assert.DoesNotContain("area.country", fields);
    }

    [Fact]
    public void Validate_DistanceWithoutWhere_IsAnError()
    {
        var d = JobbbySettings.Default;
        var s = d with { Area = d.Area with { Country = "de", DistanceKm = 20 } };

        Assert.Contains(SettingsValidator.Validate(s, forRun: false), e => e.Field == "area.distanceKm");
    }

    [Fact]
    public void Validate_ValidRunSettings_HasNoErrors()
    {
        var d = JobbbySettings.Default;
        var s = d with { Area = d.Area with { Country = "gb", Where = "Leeds", DistanceKm = 15 } };

        Assert.Empty(SettingsValidator.Validate(s, forRun: true));
    }

    [Fact]
    public void Countries_HaveCurrency()
    {
        Assert.True(AdzunaCountries.IsSupported("it"));
        Assert.Equal("EUR", AdzunaCountries.CurrencyByCode["it"]);
        Assert.Equal("GBP", AdzunaCountries.CurrencyByCode["gb"]);
        Assert.False(AdzunaCountries.IsSupported("IT"));
    }

    [Fact]
    public void RemoteSweep_AllKeywords_FlattensLanguages()
    {
        var keywords = new RemoteSweepSettings
        {
            Keywords = new() { ["it"] = new() { "da remoto" }, ["en"] = new() { "remote", " " } },
        };

        Assert.Equal(new[] { "da remoto", "remote" }, keywords.AllKeywords().OrderByDescending(k => k.Length));
    }
}
