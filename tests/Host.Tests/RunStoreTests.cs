using Config;
using CvExtraction;
using GraphEngine;
using JobPostings;
using Xunit;

namespace Host.Tests;

public class RunStoreTests
{
    private static readonly SourceDefinition Adzuna = new() { Name = "Adzuna", BaseUrl = "https://api.adzuna.com", Type = "api" };

    [Fact]
    public async Task DryRun_WritesACompleteRunFile_AndNoLedger()
    {
        using var tmp = new TempDir();
        var dataDir = new DataDir(tmp.Root);

        var summary = await Runner(dataDir, new MockJobSource(_ => new[] { Posting("https://x/1") })).RunAsync(Settings(), Cv(), RunMode.Dry, new Progress<RunEvent>(), CancellationToken.None);

        var run = new RunStore(dataDir).Load(summary.RunId)!;
        Assert.Equal("dry", run.Mode);
        Assert.Equal(RunStatus.Completed, run.Status);
        Assert.Equal("de", run.Settings.Area.Country);
        Assert.Equal(new[] { "nurse" }, run.Queries);
        Assert.Equal(1, run.AdzunaCalls);
        var query = Assert.Single(run.QueryOutcomes);
        Assert.Equal(1, query.Returned);
        var posting = Assert.Single(run.Postings);
        Assert.Equal(PostingStatus.Evaluated, posting.Status);
        Assert.Equal("Pending", posting.Record!.Outcome);
        Assert.Equal("Borderline", posting.Record.Category);
        Assert.NotNull(run.EndedAt);
        Assert.False(File.Exists(dataDir.ApplicationsPath));
        Assert.False(File.Exists(dataDir.RunReportsPath));
    }

    [Fact]
    public async Task NormalRun_WritesTheRunFile_InsteadOfRunReports()
    {
        using var tmp = new TempDir();
        var dataDir = new DataDir(tmp.Root);

        var summary = await Runner(dataDir, new MockJobSource(_ => new[] { Posting("https://x/1") })).RunAsync(Settings(), Cv(), RunMode.Normal, new Progress<RunEvent>(), CancellationToken.None);

        Assert.Equal(RunStatus.Completed, new RunStore(dataDir).Load(summary.RunId)!.Status);
        Assert.False(File.Exists(dataDir.RunReportsPath));
        Assert.Equal(summary.RunPath, Path.Combine(dataDir.RunsDirectory, summary.RunId + ".json"));
    }

    [Fact]
    public async Task CancelledRun_IsInterrupted_WithUnfinishedPostingsMarked()
    {
        using var tmp = new TempDir();
        using var cts = new CancellationTokenSource();
        var dataDir = new DataDir(tmp.Root);
        var runner = new JobbbyRunner(dataDir, new RunDependencies(new MockJobSource(_ => new[] { Posting("https://x/1") }), new BlockingOnExtraction(cts.Cancel), new[] { Adzuna }));

        var summary = await runner.RunAsync(Settings(), Cv(), RunMode.Normal, new Progress<RunEvent>(), cts.Token);

        var run = new RunStore(dataDir).Load(summary.RunId)!;
        Assert.Equal(RunStatus.Interrupted, run.Status);
        Assert.Equal(PostingStatus.Interrupted, Assert.Single(run.Postings).Status);
    }

    [Fact]
    public async Task RunThatThrows_IsRecordedAsFailed_WithTheMessage()
    {
        using var tmp = new TempDir();
        var dataDir = new DataDir(tmp.Root);
        File.WriteAllText(dataDir.SkillAliasesPath, """{"aliases":null}""");

        await Assert.ThrowsAsync<SettingsFileException>(() =>
            Runner(dataDir, new MockJobSource(_ => Array.Empty<RawPosting>())).RunAsync(Settings(), Cv(), RunMode.Dry, new Progress<RunEvent>(), CancellationToken.None));

        var run = Assert.Single(new RunStore(dataDir).List());
        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Contains("skill-aliases.json", run.Error);
    }

    [Fact]
    public void RecoverInterrupted_MarksRunsLeftRunning()
    {
        using var tmp = new TempDir();
        var store = new RunStore(new DataDir(tmp.Root));
        store.Save(new RunRecord { RunId = "r1", Mode = "normal", Status = RunStatus.Running, StartedAt = DateTimeOffset.UtcNow });
        store.Save(new RunRecord { RunId = "r2", Mode = "dry", Status = RunStatus.Completed, StartedAt = DateTimeOffset.UtcNow });

        var recovered = store.RecoverInterrupted();

        Assert.Equal(new[] { "r1" }, recovered);
        Assert.Equal(RunStatus.Interrupted, store.Load("r1")!.Status);
        Assert.Equal(RunStatus.Completed, store.Load("r2")!.Status);
    }

    [Fact]
    public void ImportLegacyReports_OnceAsSummaryOnlyRuns()
    {
        using var tmp = new TempDir();
        var dataDir = new DataDir(tmp.Root);
        File.WriteAllText(dataDir.RunReportsPath, """
            [{"RunAt":"2026-09-01T08:00:00+00:00","TotalFetched":5,"SkippedDuplicate":0,"RejectedStageOne":1,"StageTwoBreakdown":{},"AutoApproved":0,"HumanApproved":0,"HumanRejected":0,"TimedOut":0,"ErrorsPerSource":{}},
             {"RunAt":"2026-09-02T08:00:00+00:00","TotalFetched":7,"SkippedDuplicate":0,"RejectedStageOne":2,"StageTwoBreakdown":{},"AutoApproved":0,"HumanApproved":0,"HumanRejected":0,"TimedOut":0,"ErrorsPerSource":{}}]
            """);
        var store = new RunStore(dataDir);

        Assert.Equal(2, store.ImportLegacyReports());
        Assert.Equal(0, store.ImportLegacyReports());

        var runs = store.List();
        Assert.Equal(2, runs.Count);
        Assert.All(runs, run => Assert.Equal("legacy", run.Mode));
        Assert.Equal(7, runs[0].Report!.TotalFetched);
        Assert.False(File.Exists(dataDir.RunReportsPath));
    }

    [Fact]
    public void Load_UnknownOrUnsafeId_IsNull()
    {
        using var tmp = new TempDir();
        var store = new RunStore(new DataDir(tmp.Root));

        Assert.Null(store.Load("missing"));
        Assert.Null(store.Load("../settings"));
    }

    private static JobbbyRunner Runner(DataDir dataDir, IJobSource source) =>
        new(dataDir, new RunDependencies(source, new RoutedLlm(), new[] { Adzuna }));

    private static JobbbySettings Settings()
    {
        var d = JobbbySettings.Default;
        return d with { Area = d.Area with { Country = "de" }, Searches = d.Searches with { Queries = new() { "nurse" }, DeriveFromCv = false } };
    }

    private static CvData Cv() => new() { Name = "C", YearsExperience = 5, Skills = new() { "Triage" } };

    private static RawPosting Posting(string url) => new("Nurse", "Pronto soccorso", url, "api.adzuna.com", "Clinic", DateTimeOffset.UtcNow.AddHours(-1));

    private sealed class RoutedLlm : ILlmClient
    {
        public Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken = default) =>
            Task.FromResult(prompt.Contains("Estrai le seguenti informazioni", StringComparison.Ordinal)
                ? """{"seniorityLevel":"","requiredSkills":["Triage"],"workMode":"onsite"}"""
                : """{"category":"Borderline","reasoning":"r","confidence":0.5}""");
    }

    private sealed class BlockingOnExtraction(Action onCall) : ILlmClient
    {
        public async Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken = default)
        {
            onCall();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return "";
        }
    }
}
