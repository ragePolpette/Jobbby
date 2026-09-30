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
    public async Task NormalRun_RecordsTheFullPostingInTheLedger()
    {
        using var tmp = new TempDir();
        var dataDir = new DataDir(tmp.Root);
        var runner = new JobbbyRunner(dataDir, Deps(new MockJobSource(_ => new[] { Posting("Nurse", "https://x/1") })));

        var summary = await runner.RunAsync(Settings(), Cv(), RunMode.Normal, new Progress<RunEvent>(), CancellationToken.None);

        var record = new ApplicationLedger.ApplicationLedger(dataDir.ApplicationsPath).Pending().Single();
        Assert.Equal(summary.RunId, record.RunId);
        Assert.Equal("Adzuna", record.SourceName);
        Assert.Equal("https://x/1", record.ApplyUrl);
        Assert.Equal("https://x/1", record.SourceUrl);
        Assert.Equal("Reparto di pronto soccorso", record.Excerpt);
        Assert.Equal(new[] { "Triage" }, record.RequiredSkills!);
        Assert.Equal("Borderline", record.Category);
        Assert.Equal(0.5, record.Confidence);
        Assert.Equal("r", record.Reasoning);
        Assert.Equal("Onsite", record.WorkMode);
    }

    [Fact]
    public async Task NormalRun_PostingAlreadyInTheLedger_IsSkippedBeforeAnyLlmCall()
    {
        using var tmp = new TempDir();
        var dataDir = new DataDir(tmp.Root);
        var key = ApplicationLedger.PostingIdentity.Key("Clinic", "Nurse");
        new ApplicationLedger.ApplicationLedger(dataDir.ApplicationsPath).RecordOutcome(
            new ApplicationLedger.ApplicationRecord(key, "Clinic", "Nurse", null, DateTimeOffset.UtcNow, ApplicationLedger.ApplicationOutcomes.Rejected));
        var llm = new PromptRoutedLlm();
        var runner = new JobbbyRunner(dataDir, Deps(new MockJobSource(_ => new[] { Posting("Nurse", "https://x/2") }), llm));

        var summary = await runner.RunAsync(Settings(), Cv(), RunMode.Normal, new Progress<RunEvent>(), CancellationToken.None);

        Assert.Equal(0, llm.Calls);
        Assert.Equal(1, summary.Report.SkippedDuplicate);
    }

    [Fact]
    public async Task NormalRun_RecordsPendingWithoutAnyApprovalChannel()
    {
        using var tmp = new TempDir();
        var dataDir = new DataDir(tmp.Root);
        var runner = new JobbbyRunner(dataDir, Deps(new MockJobSource(_ => new[] { Posting("Nurse", "https://x/1") })));

        var summary = await runner.RunAsync(Settings(), Cv(), RunMode.Normal, new Progress<RunEvent>(), CancellationToken.None);

        Assert.Equal(1, summary.Report.Pending);
        Assert.Contains("\"Pending\"", File.ReadAllText(dataDir.ApplicationsPath));
    }

    [Fact]
    public async Task Run_Cancelled_ReportsCancelledAndWritesNoCursors()
    {
        using var tmp = new TempDir();
        using var cts = new CancellationTokenSource();
        var dataDir = new DataDir(tmp.Root);
        var llm = new PromptRoutedLlm(onExtraction: cts.Cancel, blockAfterCallback: true);
        var runner = new JobbbyRunner(dataDir, Deps(new MockJobSource(_ => new[] { Posting("Nurse", "https://x/1") }), llm));

        var summary = await runner.RunAsync(Settings(), Cv(), RunMode.Normal, new Progress<RunEvent>(), cts.Token);

        Assert.True(summary.Cancelled);
        Assert.False(File.Exists(dataDir.CursorsPath));
    }

    [Fact]
    public async Task Run_CancelledDuringQueryPlanning_ReportsCancelled()
    {
        // Regression: deriving queries from the CV happens before the fetch loop and let
        // OperationCanceledException escape RunAsync instead of returning a cancelled summary.
        using var tmp = new TempDir();
        using var cts = new CancellationTokenSource();
        var source = new MockJobSource(_ => Array.Empty<RawPosting>());
        var llm = new BlockingLlm(onCall: cts.Cancel);
        var runner = new JobbbyRunner(new DataDir(tmp.Root), Deps(source, llm));
        var settings = Settings() with { Searches = Settings().Searches with { DeriveFromCv = true } };

        var summary = await runner.RunAsync(settings, Cv(), RunMode.Dry, new Progress<RunEvent>(), cts.Token);

        Assert.True(summary.Cancelled);
        Assert.Empty(source.RequestsReceived);
    }

    [Fact]
    public async Task NormalRun_KeepsCursorsOfOtherSearches()
    {
        using var tmp = new TempDir();
        var dataDir = new DataDir(tmp.Root);
        File.WriteAllText(dataDir.CursorsPath, """[{"SourceName":"Adzuna|fr|||local|other","LastSeenIdentifier":"u","LastRunAt":"2026-09-01T00:00:00+00:00"}]""");
        var llm = new PromptRoutedLlm(judgment: """{"category":"Strong","reasoning":"r","confidence":0.9}""");
        var runner = new JobbbyRunner(dataDir, Deps(new MockJobSource(_ => new[] { Posting("Nurse", "https://x/1") }), llm));

        await runner.RunAsync(Settings(), Cv(), RunMode.Normal, new Progress<RunEvent>(), CancellationToken.None);

        var cursors = File.ReadAllText(dataDir.CursorsPath);
        Assert.Contains("Adzuna|fr|||local|other", cursors);
        Assert.Contains("Adzuna|de|||local|nurse", cursors);
    }

    [Fact]
    public async Task NormalRun_WritesCursorsAtomicallyInDataDir()
    {
        using var tmp = new TempDir();
        var dataDir = new DataDir(tmp.Root);
        // Strong above the threshold is auto-approved: no Telegram wait in PR 1's normal mode.
        var llm = new PromptRoutedLlm(judgment: """{"category":"Strong","reasoning":"r","confidence":0.9}""");
        var runner = new JobbbyRunner(dataDir, Deps(new MockJobSource(_ => new[] { Posting("Nurse", "https://x/1") }), llm));

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

    private static RunDependencies Deps(IJobSource source, ILlmClient? llm = null) =>
        new(source, llm ?? new PromptRoutedLlm(), new[] { Adzuna });

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
            // Normalization is the only prompt asking to extract fields; stage two also carries <annuncio>.
            if (prompt.Contains("Estrai le seguenti informazioni", StringComparison.Ordinal))
            {
                onExtraction?.Invoke();
                if (blockAfterCallback)
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return """{"seniorityLevel":"","requiredSkills":["Triage"],"workMode":"onsite"}""";
            }

            return judgment;
        }
    }

    private sealed class BlockingLlm(Action onCall) : ILlmClient
    {
        public async Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken = default)
        {
            onCall();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return "";
        }
    }

    private sealed class SyncProgress(Action<RunEvent> report) : IProgress<RunEvent>
    {
        public void Report(RunEvent value) => report(value);
    }
}
