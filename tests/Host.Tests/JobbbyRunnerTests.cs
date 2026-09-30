using Config;
using CvExtraction;
using GraphEngine;
using JobPostings;
using Xunit;

namespace Host.Tests;

public class JobbbyRunnerTests
{
    private static readonly SourceDefinition Adzuna = new() { Name = "Adzuna", BaseUrl = "https://api.adzuna.com", Type = "api" };

    [Fact]
    public async Task DryRun_EvaluatesWithoutWritingLedgerOrCursors()
    {
        using var tmp = new TempDir();
        var dataDir = new DataDir(tmp.Root);
        var runner = new JobbbyRunner(dataDir, Deps(new MockJobSource(_ => new[] { Posting("Nurse", "https://x/1") })));

        var summary = await runner.RunAsync(Settings(), Cv(), RunMode.Dry, new Progress<RunEvent>(), CancellationToken.None);

        Assert.False(summary.Cancelled);
        Assert.Equal(1, summary.Report.TotalFetched);
        Assert.Equal(1, summary.AdzunaCalls);
        Assert.NotNull(summary.DryRunLog);
        Assert.False(File.Exists(dataDir.ApplicationsPath));
        Assert.False(File.Exists(dataDir.CursorsPath));
    }

    [Fact]
    public async Task Run_UsesAreaAndRemoteSweepFromSettings()
    {
        using var tmp = new TempDir();
        var source = new MockJobSource(_ => Array.Empty<RawPosting>());
        var runner = new JobbbyRunner(new DataDir(tmp.Root), Deps(source));
        var settings = Settings() with
        {
            Area = new AreaSettings { Country = "de", Where = "Köln", DistanceKm = 20, AcceptsRemote = true },
            RemoteSweep = new RemoteSweepSettings { Keywords = new() { ["de"] = new() { "homeoffice" } } },
        };

        var summary = await runner.RunAsync(settings, Cv(), RunMode.Dry, new Progress<RunEvent>(), CancellationToken.None);

        Assert.Equal(new[] { SearchSweep.Local, SearchSweep.Remote }, source.RequestsReceived.Select(r => r.Sweep));
        Assert.Equal("Köln", source.RequestsReceived[0].Where);
        Assert.Equal(2, summary.AdzunaCalls);
    }

    [Fact]
    public async Task Run_InvalidSettingsForRun_FailsBeforeAnyCall()
    {
        using var tmp = new TempDir();
        var source = new MockJobSource(_ => Array.Empty<RawPosting>());
        var llm = new PromptRoutedLlm();
        var runner = new JobbbyRunner(new DataDir(tmp.Root), Deps(source, llm));

        var error = await Assert.ThrowsAsync<SettingsValidationException>(() =>
            runner.RunAsync(JobbbySettings.Default, Cv(), RunMode.Dry, new Progress<RunEvent>(), CancellationToken.None));

        Assert.Contains(error.Errors, e => e.Field == "area.country");
        Assert.Empty(source.RequestsReceived);
        Assert.Equal(0, llm.Calls);
    }

    [Fact]
    public async Task NormalRun_WithoutApprovalChannel_FailsBeforeAnyCall()
    {
        using var tmp = new TempDir();
        var source = new MockJobSource(_ => Array.Empty<RawPosting>());
        var runner = new JobbbyRunner(new DataDir(tmp.Root), Deps(source));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runner.RunAsync(Settings(), Cv(), RunMode.Normal, new Progress<RunEvent>(), CancellationToken.None));

        Assert.Empty(source.RequestsReceived);
    }

    [Fact]
    public async Task Run_Cancelled_ReportsCancelledAndWritesNoCursors()
    {
        using var tmp = new TempDir();
        using var cts = new CancellationTokenSource();
        var dataDir = new DataDir(tmp.Root);
        var llm = new PromptRoutedLlm(onExtraction: cts.Cancel, blockAfterCallback: true);
        var runner = new JobbbyRunner(dataDir, Deps(new MockJobSource(_ => new[] { Posting("Nurse", "https://x/1") }), llm, new Notifications.MockTelegramGateway()));

        var summary = await runner.RunAsync(Settings(), Cv(), RunMode.Normal, new Progress<RunEvent>(), cts.Token);

        Assert.True(summary.Cancelled);
        Assert.False(File.Exists(dataDir.CursorsPath));
    }

    [Fact]
    public async Task NormalRun_WritesCursorsAtomicallyInDataDir()
    {
        using var tmp = new TempDir();
        var dataDir = new DataDir(tmp.Root);
        // Strong above the threshold is auto-approved: no Telegram wait in PR 1's normal mode.
        var llm = new PromptRoutedLlm(judgment: """{"category":"Strong","reasoning":"r","confidence":0.9}""");
        var runner = new JobbbyRunner(dataDir, Deps(new MockJobSource(_ => new[] { Posting("Nurse", "https://x/1") }), llm, new Notifications.MockTelegramGateway()));

        var summary = await runner.RunAsync(Settings(), Cv(), RunMode.Normal, new Progress<RunEvent>(), CancellationToken.None);

        Assert.False(summary.Cancelled);
        Assert.Contains("Adzuna|de|||local|nurse", File.ReadAllText(dataDir.CursorsPath));
        Assert.DoesNotContain(Directory.GetFiles(tmp.Root), f => f.Contains(".tmp-"));
    }

    [Fact]
    public async Task Run_DiscoveryConfigured_IsIgnoredWithWarning()
    {
        using var tmp = new TempDir();
        var runner = new JobbbyRunner(new DataDir(tmp.Root), Deps(new MockJobSource(_ => Array.Empty<RawPosting>())), discoveryConfigured: true);

        var summary = await runner.RunAsync(Settings(), Cv(), RunMode.Dry, new Progress<RunEvent>(), CancellationToken.None);

        Assert.Contains(summary.Warnings, w => w.Contains("Discovery"));
    }

    [Fact]
    public async Task Run_ReportsProgressEvents()
    {
        using var tmp = new TempDir();
        var events = new List<RunEvent>();
        var runner = new JobbbyRunner(new DataDir(tmp.Root), Deps(new MockJobSource(_ => new[] { Posting("Nurse", "https://x/1") })));

        await runner.RunAsync(Settings(), Cv(), RunMode.Dry, new SyncProgress(events.Add), CancellationToken.None);

        Assert.Contains(events, e => e.Kind == "fetched");
        Assert.Contains(events, e => e.Kind == "evaluated");
        Assert.Contains(events, e => e.Kind == "completed");
    }

    private static JobbbySettings Settings()
    {
        var d = JobbbySettings.Default;
        return d with
        {
            Area = d.Area with { Country = "de" },
            Searches = d.Searches with { Queries = new() { "nurse" }, DeriveFromCv = false },
        };
    }

    private static CvData Cv() => new() { Name = "Candidate", YearsExperience = 5, Skills = new() { "Triage" } };

    private static RunDependencies Deps(IJobSource source, ILlmClient? llm = null, Notifications.ITelegramGateway? gateway = null) =>
        new(source, llm ?? new PromptRoutedLlm(), gateway, new[] { Adzuna });

    private static RawPosting Posting(string title, string url) =>
        new(title, "Reparto di pronto soccorso", url, "api.adzuna.com", "Clinic", DateTimeOffset.UtcNow.AddHours(-1));

    /// <summary>Answers normalization and judgment prompts with fixed JSON; optionally cancels and blocks on the first extraction.</summary>
    private sealed class PromptRoutedLlm(
        Action? onExtraction = null,
        bool blockAfterCallback = false,
        string judgment = """{"category":"Borderline","reasoning":"r","confidence":0.5}""") : ILlmClient
    {
        public int Calls;

        public async Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Calls);
            if (prompt.Contains("<annuncio>", StringComparison.Ordinal))
            {
                onExtraction?.Invoke();
                if (blockAfterCallback)
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return """{"seniorityLevel":"","requiredStack":["Triage"],"workMode":"onsite"}""";
            }

            return judgment;
        }
    }

    private sealed class SyncProgress(Action<RunEvent> report) : IProgress<RunEvent>
    {
        public void Report(RunEvent value) => report(value);
    }
}
