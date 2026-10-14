using ApplicationLedger;
using Config;
using CvExtraction;
using GraphEngine;
using Host.Nodes;
using JobPostings;
using Reporting;
using Xunit;

namespace Host.Tests;

/// <summary>Regressions for the PR 3 final review.</summary>
public class Pr3ReviewFixTests
{
    [Fact]
    public void Migration_DoesNotBringBackRunReportsAlreadyImported()
    {
        using var legacy = new TempDir();
        using var data = new TempDir();
        File.WriteAllText(legacy.Path("run-reports.json"), "[]");
        var dataDir = new DataDir(data.Root);

        LegacyMigration.Run(dataDir, new[] { legacy.Root });
        new RunStore(dataDir).ImportLegacyReports();
        var second = LegacyMigration.Run(dataDir, new[] { legacy.Root });

        Assert.Empty(second);
        Assert.False(File.Exists(dataDir.RunReportsPath));
    }

    [Fact]
    public void ImportLegacyReports_CorruptFile_WarnsInsteadOfFailing()
    {
        using var data = new TempDir();
        var dataDir = new DataDir(data.Root);
        File.WriteAllText(dataDir.RunReportsPath, "[ not json");

        var result = new RunStore(dataDir).ImportLegacyReports();

        Assert.Equal(0, result.Imported);
        Assert.Contains("run-reports.json", result.Warning);
    }

    [Fact]
    public void Key_WithoutCompany_IncludesTheLink()
    {
        Assert.NotEqual(PostingIdentity.Key("", "Backend Developer", applyUrl: "https://x/1"), PostingIdentity.Key("", "Backend Developer", applyUrl: "https://x/2"));
        Assert.Equal(PostingIdentity.Key("", "Backend Developer", applyUrl: "https://x/1"), PostingIdentity.Key(" ", "backend developer", applyUrl: "https://x/1"));
        Assert.Equal(PostingIdentity.Key("Acme", "Dev", applyUrl: "https://x/1"), PostingIdentity.Key("Acme", "Dev", applyUrl: "https://x/2"));
    }

    [Fact]
    public async Task PostingsWithoutCompany_AreNotMergedAcrossRunsByTitleAlone()
    {
        using var tmp = new TempDir();
        var ledger = new ApplicationLedger.ApplicationLedger(tmp.Path("applications.json"));

        var first = await RunGraphAsync(ledger, "https://x/1");
        var otherEmployer = await RunGraphAsync(ledger, "https://x/2");
        var sameAd = await RunGraphAsync(ledger, "https://x/1");

        Assert.False(first.Get<bool>(DedupeCheckNode.AlreadyAppliedStateKey));
        Assert.False(otherEmployer.Get<bool>(DedupeCheckNode.AlreadyAppliedStateKey));
        Assert.True(sameAd.Get<bool>(DedupeCheckNode.AlreadyAppliedStateKey));
        Assert.Equal(2, ledger.Pending().Count);
    }

    [Fact]
    public async Task CompanyFromTheLlm_NeverEntersTheIdentity_SoItCannotHideAPostingBehindAnotherOne()
    {
        using var tmp = new TempDir();
        var dataDir = new DataDir(tmp.Root);
        var key = PostingIdentity.Key("Clinic", "Nurse");
        var seeded = new ApplicationLedger.ApplicationLedger(dataDir.ApplicationsPath);
        seeded.RecordOutcome(new ApplicationRecord(key, "Clinic", "Nurse", null, DateTimeOffset.UtcNow, ApplicationOutcomes.Rejected));
        seeded.Flush();
        var source = new MockJobSource(_ => new[] { new RawPosting("Nurse", "Pronto soccorso", "https://x/9", "api.adzuna.com", "", DateTimeOffset.UtcNow) });
        var runner = new JobbbyRunner(dataDir, new RunDependencies(source, new CompanyFromLlm("Clinic"), Sources));

        var summary = await runner.RunAsync(Settings(), Cv, RunMode.Dry, new Progress<RunEvent>(), CancellationToken.None);

        Assert.Equal(0, summary.Report.SkippedDuplicate);
        Assert.Equal(PostingStatus.Evaluated, Assert.Single(LoadPostings(dataDir, summary.RunId)).Status);
    }

    [Fact]
    public async Task PostingWithoutCompany_IsRecognizedInTheNextRun_WhateverTheLlmAnswers_WithoutCallingIt()
    {
        using var tmp = new TempDir();
        var dataDir = new DataDir(tmp.Root);
        var source = new MockJobSource(_ => new[] { new RawPosting("Nurse", "Pronto soccorso", "https://x/9?utm=a", "api.adzuna.com", "", DateTimeOffset.UtcNow, SourceId: "42") });

        await new JobbbyRunner(dataDir, new RunDependencies(source, new CompanyFromLlm("Clinic"), Sources))
            .RunAsync(Settings(), Cv, RunMode.Normal, new Progress<RunEvent>(), CancellationToken.None);
        var secondLlm = new CompanyFromLlm("Another Clinic");
        var summary = await new JobbbyRunner(dataDir, new RunDependencies(source, secondLlm, Sources))
            .RunAsync(Settings(), Cv, RunMode.Normal, new Progress<RunEvent>(), CancellationToken.None);

        Assert.Equal(1, summary.Report.SkippedDuplicate);
        Assert.Equal(0, secondLlm.Calls);
        var record = Assert.Single(new ApplicationLedger.ApplicationLedger(dataDir.ApplicationsPath).Pending());
        Assert.Equal("Clinic", record.Company);
        Assert.Equal(string.Empty, record.SourceCompany);
        Assert.Equal("42", record.SourcePostingId);
    }

    private static readonly SourceDefinition[] Sources = { new() { Name = "Adzuna", BaseUrl = "https://api.adzuna.com" } };

    private static readonly CvData Cv = new() { Name = "C", Skills = new() { "Triage" } };

    private static JobbbySettings Settings()
    {
        var d = JobbbySettings.Default;
        return d with { Area = d.Area with { Country = "de" }, Searches = d.Searches with { Queries = new() { "nurse" }, DeriveFromCv = false } };
    }

    private static List<RunPosting> LoadPostings(DataDir dataDir, string runId) => new RunStore(dataDir).Load(runId)!.Postings;

    private static async Task<GraphState> RunGraphAsync(ApplicationLedger.ApplicationLedger ledger, string applyUrl)
    {
        var definition = HostGraph.Build(ledger,
            new MockLlmClient("""{"category":"Borderline","reasoning":"r","confidence":0.4}"""),
            new CvData { Name = "C", Skills = new() { "Triage" } }, new RunStatsCollector());
        var posting = new JobPosting("Backend Developer", "", "", new List<string> { "Triage" }, "d", "https://x", applyUrl, ApplyChannels.ExternalPlatform);
        var state = new GraphState(new Dictionary<string, object>
        {
            [NormalizeJobPostingNode.JobPostingStateKey] = posting,
            [JobApplicationStateKeys.SourceUrl] = applyUrl,
        });
        await definition.CreateRun().RunAsync(HostGraph.DedupeCheckNodeName, state);
        return state;
    }

    private sealed class CompanyFromLlm(string company) : ILlmClient
    {
        private int _calls;

        public int Calls => _calls;

        public Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(prompt.Contains("Extract the following information", StringComparison.Ordinal)
                ? $$"""{"company":"{{company}}","seniorityLevel":"","requiredSkills":["Triage"]}"""
                : """{"category":"Borderline","reasoning":"r","confidence":0.4}""");
        }
    }
}
