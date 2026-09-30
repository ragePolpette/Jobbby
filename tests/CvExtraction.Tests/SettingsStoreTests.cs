using Config;
using Xunit;

namespace CvExtraction.Tests;

public class SettingsStoreTests
{
    [Fact]
    public void LoadOrCreate_Missing_CreatesDefaultsSeededFromLegacySearches()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.Path("searches.json"), """{"queries":["q1"],"deriveFromCv":false,"maxDerivedQueries":2}""");

        var result = SettingsStore.LoadOrCreate(dir.Path("settings.json"), dir.Path("searches.json"));

        Assert.True(result.Created);
        Assert.Equal(new[] { "q1" }, result.Settings.Searches.Queries);
        Assert.False(result.Settings.Searches.DeriveFromCv);
        Assert.Equal(2, result.Settings.Searches.MaxDerivedQueries);
        Assert.Null(result.Settings.Area.Country);
        Assert.True(File.Exists(dir.Path("settings.json")));
    }

    [Fact]
    public void LoadOrCreate_MissingWithoutLegacy_CreatesDefaults()
    {
        using var dir = new TempDir();

        var result = SettingsStore.LoadOrCreate(dir.Path("settings.json"), dir.Path("does-not-exist.json"));

        Assert.True(result.Created);
        Assert.Equal(JobbbySettings.Default.Searches.Queries, result.Settings.Searches.Queries);
    }

    [Fact]
    public void Save_ThenLoad_RoundTrips()
    {
        using var dir = new TempDir();
        var d = JobbbySettings.Default;
        var settings = d with
        {
            Area = d.Area with { Country = "it", Where = "Reggio nell'Emilia", DistanceKm = 30, AcceptsRemote = true },
            Salary = d.Salary with { MinimumYearly = 33000 },
            RemoteSweep = new RemoteSweepSettings { Keywords = new() { ["it"] = new() { "da remoto" } } },
        };

        SettingsStore.Save(dir.Path("settings.json"), settings);
        var loaded = SettingsStore.LoadOrCreate(dir.Path("settings.json"), null);

        Assert.False(loaded.Created);
        Assert.Equal("Reggio nell'Emilia", loaded.Settings.Area.Where);
        Assert.Equal(33000m, loaded.Settings.Salary.MinimumYearly);
        Assert.Equal(new[] { "da remoto" }, loaded.Settings.RemoteSweep.Keywords["it"]);
    }

    [Fact]
    public void LoadOrCreate_WrongType_ThrowsWithFieldName()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.Path("settings.json"), """{"area":{"distanceKm":"trenta"}}""");

        var error = Assert.Throws<SettingsFileException>(() => SettingsStore.LoadOrCreate(dir.Path("settings.json"), null));

        Assert.Contains("distanceKm", error.Message);
    }

    [Fact]
    public void LoadOrCreate_MalformedJson_ThrowsSettingsFileException()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.Path("settings.json"), """{"area": """);

        Assert.Throws<SettingsFileException>(() => SettingsStore.LoadOrCreate(dir.Path("settings.json"), null));
    }

    [Fact]
    public void LoadOrCreate_UnknownFields_AreReportedAsNotes()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.Path("settings.json"), """{"area":{"country":"it","wher":"Milano"},"extra":1}""");

        var result = SettingsStore.LoadOrCreate(dir.Path("settings.json"), null);

        Assert.Equal("it", result.Settings.Area.Country);
        Assert.Contains(result.Notes, n => n.Contains("area.wher"));
        Assert.Contains(result.Notes, n => n.Contains("extra"));
    }

    [Fact]
    public void LoadOrCreate_DictionaryKeys_AreNotReportedAsUnknown()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.Path("settings.json"), """{"remoteSweep":{"keywords":{"de":["homeoffice"]}}}""");

        var result = SettingsStore.LoadOrCreate(dir.Path("settings.json"), null);

        Assert.Empty(result.Notes);
    }

    [Fact]
    public void Save_IsAtomic_LeavesNoTemporaryFile()
    {
        using var dir = new TempDir();

        SettingsStore.Save(dir.Path("settings.json"), JobbbySettings.Default);
        SettingsStore.Save(dir.Path("settings.json"), JobbbySettings.Default);

        Assert.Equal(new[] { dir.Path("settings.json") }, Directory.GetFiles(dir.Root));
    }
}
