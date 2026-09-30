using System.Globalization;
using ApplicationLedger;
using CvExtraction;
using GraphEngine;
using Host.Nodes;
using JobPostings;
using Matching;
using Notifications;
using Reporting;
using Xunit;

namespace Host.Tests;

/// <summary>The asynchronous flow (no approval gateway): every evaluated posting gets an outcome, nobody waits.</summary>
public class HostGraphOutcomeTests
{
    private static readonly CvData Cv = new() { Name = "C", YearsExperience = 4, Skills = new() { "Triage" } };

    [Fact]
    public async Task StageOneFailed_IsAutoRejected_WithoutCallingTheJudge()
    {
        var (outcome, state, stats, ledger) = await RunAsync(Posting(new[] { "Contabilità" }), new MockLlmClient("not json"));

        Assert.Equal(ApplicationOutcomes.AutoRejected, outcome);
        Assert.False(state.ContainsKey(ScoreMatchNode.MatchJudgmentReasoningStateKey));
        Assert.Equal(1, stats.BuildReport(DateTimeOffset.UtcNow).AutoRejected);
        Assert.Equal(ApplicationOutcomes.AutoRejected, ledger.Current(PostingIdentity.Id(PostingIdentity.Key("Clinic", "Nurse")))!.Outcome);
    }

    [Fact]
    public async Task Weak_IsAutoRejected_KeepingTheJudgesConfidence()
    {
        var (outcome, state, _, _) = await RunAsync(Posting(new[] { "Triage" }), Judgment(MatchCategories.Weak, 0.95));

        Assert.Equal(ApplicationOutcomes.AutoRejected, outcome);
        Assert.Equal(0.95, state.Get<double>(ScoreMatchNode.MatchConfidenceStateKey));
        Assert.Equal(MatchCategories.Weak, state.Get<string>(ScoreMatchNode.MatchCategoryStateKey));
    }

    [Theory]
    [InlineData(MatchCategories.Borderline, 0.3, ApplicationOutcomes.Pending)]
    [InlineData(MatchCategories.Borderline, 0.05, ApplicationOutcomes.Pending)]
    [InlineData(MatchCategories.Strong, 0.5, ApplicationOutcomes.Pending)]
    [InlineData(MatchCategories.Strong, 0.7, ApplicationOutcomes.Shortlisted)]
    [InlineData(MatchCategories.Borderline, 0.9, ApplicationOutcomes.Shortlisted)]
    public async Task StrongOrBorderline_ArePendingBelowTheThreshold_ShortlistedFromIt(string category, double confidence, string expected)
    {
        var (outcome, _, stats, _) = await RunAsync(Posting(new[] { "Triage" }), Judgment(category, confidence));

        Assert.Equal(expected, outcome);
        var report = stats.BuildReport(DateTimeOffset.UtcNow);
        Assert.Equal(expected == ApplicationOutcomes.Pending ? 1 : 0, report.Pending);
        Assert.Equal(expected == ApplicationOutcomes.Shortlisted ? 1 : 0, report.AutoApproved);
    }

    [Fact]
    public async Task NothingExtractable_IsPending_ForInsufficientInformation()
    {
        var (outcome, state, stats, _) = await RunAsync(Posting(Array.Empty<string>()), new MockLlmClient("not json"));

        Assert.Equal(ApplicationOutcomes.Pending, outcome);
        Assert.Contains("informazioni insufficienti", state.Get<string>(RecordOutcomeNode.ReasonStateKey));
        Assert.True(state.Get<bool>(ScoreMatchNode.InsufficientInformationStateKey));
        Assert.Equal(1, stats.BuildReport(DateTimeOffset.UtcNow).Pending);
    }

    [Fact]
    public async Task NothingExtractable_ButWrongPlace_IsStillAutoRejected()
    {
        var posting = Posting(Array.Empty<string>()) with { Location = "Roma", WorkMode = WorkMode.Onsite };
        var criteria = new StageOneCriteria("Milano", GeoFilteredBySource: false, AcceptsRemote: true, MinimumYearlySalary: null);

        var (outcome, _, _, _) = await RunAsync(posting, new MockLlmClient("not json"), criteria: criteria);

        Assert.Equal(ApplicationOutcomes.AutoRejected, outcome);
    }

    [Fact]
    public async Task DryRun_ComputesTheOutcome_WithoutWritingTheLedger()
    {
        var ledgerPath = Path.Combine(Path.GetTempPath(), $"applications-{Guid.NewGuid():N}.json");

        var (outcome, _, _, _) = await RunAsync(Posting(new[] { "Triage" }), Judgment(MatchCategories.Strong, 0.9), dryRun: true, ledgerPath: ledgerPath);

        Assert.Equal(ApplicationOutcomes.Shortlisted, outcome);
        Assert.False(File.Exists(ledgerPath));
    }

    [Fact]
    public async Task Pending_IsTerminalForDedupe_InTheNextRun()
    {
        var ledgerPath = Path.Combine(Path.GetTempPath(), $"applications-{Guid.NewGuid():N}.json");
        try
        {
            await RunAsync(Posting(new[] { "Triage" }), Judgment(MatchCategories.Borderline, 0.4), ledgerPath: ledgerPath);

            var (outcome, state, _, _) = await RunAsync(Posting(new[] { "Triage" }), new MockLlmClient("not json"), ledgerPath: ledgerPath);

            Assert.True(state.Get<bool>(DedupeCheckNode.AlreadyAppliedStateKey));
            Assert.Null(outcome);
        }
        finally
        {
            File.Delete(ledgerPath);
        }
    }

    private static JobPosting Posting(IEnumerable<string> requiredSkills) =>
        new("Nurse", "Clinic", "", requiredSkills.ToList(), "desc", "https://x", "https://x/1", ApplyChannels.ExternalPlatform);

    private static ILlmClient Judgment(string category, double confidence) =>
        new MockLlmClient($$"""{"category":"{{category}}","reasoning":"r","confidence":{{confidence.ToString(CultureInfo.InvariantCulture)}}}""");

    private static async Task<(string? Outcome, GraphState State, RunStatsCollector Stats, ApplicationLedger.ApplicationLedger Ledger)> RunAsync(
        JobPosting posting, ILlmClient llm, bool dryRun = false, string? ledgerPath = null, StageOneCriteria? criteria = null)
    {
        ledgerPath ??= Path.Combine(Path.GetTempPath(), $"applications-{Guid.NewGuid():N}.json");
        var ledger = new ApplicationLedger.ApplicationLedger(ledgerPath);
        var stats = new RunStatsCollector();
        var definition = HostGraph.Build(null, new PendingApprovalRegistry(), ledger, llm, Cv, stats, dryRun: dryRun, stageOneCriteria: criteria);
        var state = new GraphState(new Dictionary<string, object>
        {
            [NormalizeJobPostingNode.JobPostingStateKey] = posting,
            [JobApplicationStateKeys.Company] = posting.Company,
            [JobApplicationStateKeys.Title] = posting.Title,
            [JobApplicationStateKeys.SourceUrl] = posting.ApplyUrl,
        });

        await definition.CreateRun().RunAsync(HostGraph.DedupeCheckNodeName, state);

        return (state.TryGet<string>(RecordOutcomeNode.OutcomeStateKey, out var outcome) ? outcome : null, state, stats, ledger);
    }
}
