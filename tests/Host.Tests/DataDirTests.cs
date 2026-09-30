using Microsoft.Extensions.Configuration;
using Xunit;

namespace Host.Tests;

public class DataDirTests
{
    [Fact]
    public void FromConfiguration_MissingSetting_ThrowsClearError()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();

        var error = Assert.Throws<InvalidOperationException>(() => DataDir.FromConfiguration(configuration));

        Assert.Contains("Jobbby:DataDir", error.Message);
    }

    [Fact]
    public void FromConfiguration_CreatesTheDirectory()
    {
        using var tmp = new TempDir();
        var root = tmp.Path("data");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Jobbby:DataDir"] = root })
            .Build();

        var dataDir = DataDir.FromConfiguration(configuration);

        Assert.Equal(Path.GetFullPath(root), dataDir.Root);
        Assert.True(Directory.Exists(root));
        Assert.Equal(Path.Combine(dataDir.Root, "settings.json"), dataDir.SettingsPath);
    }

    [Fact]
    public void Lock_SecondAcquire_FailsWithClearMessage()
    {
        using var tmp = new TempDir();
        var dataDir = new DataDir(tmp.Root);
        using var first = DataDirLock.Acquire(dataDir);

        var error = Assert.Throws<DataDirLockedException>(() => DataDirLock.Acquire(dataDir));

        Assert.Contains("già in uso", error.Message);
        Assert.Contains(tmp.Root, error.Message);
    }

    [Fact]
    public void Lock_Released_CanBeAcquiredAgain()
    {
        using var tmp = new TempDir();
        var dataDir = new DataDir(tmp.Root);

        DataDirLock.Acquire(dataDir).Dispose();
        using var again = DataDirLock.Acquire(dataDir);
    }

    [Fact]
    public void Migration_CopiesLedgerAndReportsOnce_KeepsOriginals_SkipsCursors()
    {
        using var legacy = new TempDir();
        using var data = new TempDir();
        File.WriteAllText(legacy.Path("applications.json"), "[1]");
        File.WriteAllText(legacy.Path("run-reports.json"), "[2]");
        File.WriteAllText(legacy.Path("cursors.json"), "[3]");
        var dataDir = new DataDir(data.Root);

        var notes = LegacyMigration.Run(dataDir, new[] { legacy.Root });
        var second = LegacyMigration.Run(dataDir, new[] { legacy.Root });

        Assert.Equal("[1]", File.ReadAllText(dataDir.ApplicationsPath));
        Assert.Equal("[2]", File.ReadAllText(dataDir.RunReportsPath));
        Assert.False(File.Exists(dataDir.CursorsPath));
        Assert.True(File.Exists(legacy.Path("applications.json")));
        Assert.Equal(2, notes.Count);
        Assert.Empty(second);
    }

    [Fact]
    public void Migration_NeverOverwritesExistingDataDirFiles()
    {
        using var legacy = new TempDir();
        using var data = new TempDir();
        File.WriteAllText(legacy.Path("applications.json"), "[\"old\"]");
        var dataDir = new DataDir(data.Root);
        File.WriteAllText(dataDir.ApplicationsPath, "[\"current\"]");

        var notes = LegacyMigration.Run(dataDir, new[] { legacy.Root, "/does/not/exist" });

        Assert.Equal("[\"current\"]", File.ReadAllText(dataDir.ApplicationsPath));
        Assert.Empty(notes);
    }
}
