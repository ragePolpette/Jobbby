using System.Globalization;
using ApplicationLedger;
using CvExtraction;
using GraphEngine;
using Host.Nodes;
using JobPostings;
using Matching;
using Reporting;
using Xunit;

namespace Host.Tests;

public class HostGraphTests
{
    private static readonly CvData DefaultCandidateCv = new()
    {
        Name = "Test Candidate",
        YearsExperience = 4,
        Seniority = "Mid",
        Roles = new List<CvRole>(),
        Skills = new List<string> { "C#", ".NET" },
        Languages = new List<string> { "English" },
    };

    private static ILlmClient NewJudgmentLlmClient(string category, double confidence, string reasoning = "test reasoning") =>
        new MockLlmClient(
            $$"""{"category":"{{category}}","reasoning":"{{reasoning}}","confidence":{{confidence.ToString(CultureInfo.InvariantCulture)}}}""");

    private static GraphState NewApplicationState(string company = "Acme", string title = "Backend Engineer")
    {
        var jobPosting = new JobPosting(
            title, company, "Mid", new List<string> { "C#" }, "desc", "https://example.com", "https://example.com/apply", ApplyChannels.ExternalPlatform);

        return new GraphState(new Dictionary<string, object>
        {
            [NormalizeJobPostingNode.JobPostingStateKey] = jobPosting,
            [JobApplicationStateKeys.Company] = company,
            [JobApplicationStateKeys.Title] = title,
            [JobApplicationStateKeys.SourceUrl] = "https://example.com",
        });
    }

    private static string NewLedgerPath() => Path.Combine(Path.GetTempPath(), $"applications-{Guid.NewGuid():N}.json");

    [Fact]
    public async Task AlreadyDecided_RoutesStraightToEnd_WithoutCallingTheLlm()
    {
        var ledgerPath = NewLedgerPath();
        try
        {
            var ledger = new ApplicationLedger.ApplicationLedger(ledgerPath);
            var dedupeKey = DedupeKey.Normalize("Acme", "Backend Engineer");
            ledger.RecordOutcome(new ApplicationRecord(dedupeKey, "Acme", "Backend Engineer", null, DateTimeOffset.UtcNow, ApplicationOutcomes.Applied));

            var llmClient = new MockLlmClient("not valid judgment json");
            var definition = HostGraph.Build(ledger, llmClient, DefaultCandidateCv, new RunStatsCollector());
            var state = NewApplicationState();

            var result = await definition.CreateRun().RunAsync(HostGraph.DedupeCheckNodeName, state);

            Assert.Equal(1, result.StepsExecuted);
            Assert.True(state.Get<bool>(DedupeCheckNode.AlreadyAppliedStateKey));
            Assert.False(state.ContainsKey(RecordOutcomeNode.OutcomeStateKey));
        }
        finally
        {
            File.Delete(ledgerPath);
        }
    }

    [Fact]
    public async Task FullPipeline_FromRawPosting_NormalizesThenMatchesThenShortlists()
    {
        var ledgerPath = NewLedgerPath();
        try
        {
            var ledger = new ApplicationLedger.ApplicationLedger(ledgerPath);
            const string extractionJson = """{"seniorityLevel":"Mid","requiredSkills":["C#",".NET"]}""";
            const string judgmentJson = """{"category":"Strong","reasoning":"Great overlap","confidence":0.95}""";
            var llmClient = new MockLlmClient(new[] { extractionJson, judgmentJson });
            var definition = HostGraph.Build(ledger, llmClient, DefaultCandidateCv, new RunStatsCollector(), confidenceThreshold: 0.7);
            var rawPosting = new RawPosting(
                "Backend Engineer", "Ottima opportunita in C#", "https://apply.example/jobs/1", "apply.example", "Acme Corp");
            var state = new GraphState(new Dictionary<string, object>
            {
                [NormalizeJobPostingNode.RawPostingStateKey] = rawPosting,
                [JobApplicationStateKeys.SourceUrl] = "https://apply.example",
            });

            await definition.CreateRun().RunAsync(HostGraph.NormalizeJobPostingNodeName, state);

            var jobPosting = state.Get<JobPosting>(NormalizeJobPostingNode.JobPostingStateKey);
            Assert.NotNull(jobPosting);
            Assert.Equal(ApplyChannels.NativeForm, jobPosting!.ApplyChannel);
            Assert.True(state.Get<bool>(ScoreMatchNode.StageOnePassedStateKey));
            Assert.Equal(ApplicationOutcomes.Shortlisted, state.Get<string>(RecordOutcomeNode.OutcomeStateKey));
            Assert.True(ledger.HasBeenProcessed(DedupeKey.Normalize("Acme Corp", "Backend Engineer")));
        }
        finally
        {
            File.Delete(ledgerPath);
        }
    }

    [Fact]
    public async Task AfterAnOutcome_SamePostingInASecondRun_IsDiscardedByDedupeCheck()
    {
        var ledgerPath = NewLedgerPath();
        try
        {
            var ledger = new ApplicationLedger.ApplicationLedger(ledgerPath);
            var definition = HostGraph.Build(ledger, NewJudgmentLlmClient(MatchCategories.Borderline, 0.3), DefaultCandidateCv, new RunStatsCollector());

            var firstState = NewApplicationState();
            await definition.CreateRun().RunAsync(HostGraph.DedupeCheckNodeName, firstState);
            Assert.Equal(ApplicationOutcomes.Pending, firstState.Get<string>(RecordOutcomeNode.OutcomeStateKey));

            var secondState = NewApplicationState();
            var secondResult = await definition.CreateRun().RunAsync(HostGraph.DedupeCheckNodeName, secondState);

            Assert.Equal(1, secondResult.StepsExecuted);
            Assert.True(secondState.Get<bool>(DedupeCheckNode.AlreadyAppliedStateKey));
        }
        finally
        {
            File.Delete(ledgerPath);
        }
    }
}
