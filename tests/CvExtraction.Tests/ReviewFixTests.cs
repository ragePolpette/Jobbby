using ApplicationLedger;
using Config;
using Xunit;

namespace CvExtraction.Tests;

/// <summary>Regressions for the PR 1 final review (settings nulls, atomic ledger).</summary>
public class ReviewFixTests
{
    [Theory]
    [InlineData("""{"area":{"country":"it","where":null}}""", "area.where")]
    [InlineData("""{"area":null}""", "area")]
    [InlineData("""{"remoteSweep":{"keywords":{"it":null}}}""", "remoteSweep.keywords.it")]
    [InlineData("""{"searches":{"queries":null}}""", "searches.queries")]
    [InlineData("""{"searches":{"queries":["ok",null]}}""", "searches.queries")]
    [InlineData("""{"llm":{"provider":null}}""", "llm.provider")]
    public void LoadOrCreate_ExplicitNullForNonNullableField_ThrowsNamingTheField(string json, string field)
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.Path("settings.json"), json);

        var error = Assert.Throws<SettingsFileException>(() => SettingsStore.LoadOrCreate(dir.Path("settings.json"), null));

        Assert.Contains(field, error.Message);
    }

    [Fact]
    public void LoadOrCreate_NullForNullableFields_IsFine()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.Path("settings.json"), """{"area":{"country":null,"distanceKm":null},"salary":{"minimumYearly":null},"llm":{"model":null}}""");

        var result = SettingsStore.LoadOrCreate(dir.Path("settings.json"), null);

        Assert.Null(result.Settings.Area.Country);
    }

    [Fact]
    public void Ledger_ReplacesFileAtomically_OldReadersKeepTheOldContent()
    {
        using var dir = new TempDir();
        var path = dir.Path("applications.json");
        var ledger = new ApplicationLedger.ApplicationLedger(path);
        ledger.RecordOutcome(new ApplicationRecord("a::b", "a", "b", null, DateTimeOffset.UtcNow, ApplicationOutcomes.Rejected));
        var before = File.ReadAllText(path);

        // An in-place rewrite would show the new bytes through this handle; a temp + rename
        // leaves it on the old file, which is what makes a crash mid-write harmless.
        using var oldHandle = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        ledger.RecordOutcome(new ApplicationRecord("c::d", "c", "d", null, DateTimeOffset.UtcNow, ApplicationOutcomes.Rejected));

        using var reader = new StreamReader(oldHandle);
        Assert.Equal(before, reader.ReadToEnd());
        Assert.Contains("c::d", File.ReadAllText(path));
        Assert.Single(Directory.GetFiles(dir.Root));
    }
}
