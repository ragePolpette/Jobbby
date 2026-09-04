using CvExtraction;
using GraphEngine;
using Host.Nodes;
using JobPostings;
using Matching;
using Reporting;
using Xunit;

namespace Host.Tests;

public class ScoreMatchNodeTests
{
    private static readonly CvData Cv = new()
    {
        Name = "Test Candidate",
        YearsExperience = 4,
        Seniority = "Mid",
        Roles = new List<CvRole>(),
        Skills = new List<string> { "C#", ".NET" },
        Languages = new List<string> { "English" },
    };

    private static GraphState NewStateFor(JobPosting jobPosting) =>
        new(new Dictionary<string, object> { [NormalizeJobPostingNode.JobPostingStateKey] = jobPosting });

    [Fact]
    public async Task ExecuteAsync_StageOneFails_WritesZeroConfidenceWithoutCallingTheJudge()
    {
        // No overlap with Cv's skills (C#, .NET).
        var jobPosting = new JobPosting(
            "Rust Engineer", "Acme", "Mid", new List<string> { "Rust" }, "desc", "https://x.example", "https://x.example/apply", ApplyChannels.ExternalPlatform);

        // Would throw if MatchStageTwoJudge actually tried to parse it - proves stage two never runs.
        var judge = new MatchStageTwoJudge(new MockLlmClient("not valid judgment json"));
        var statsCollector = new RunStatsCollector();
        var node = new ScoreMatchNode(Cv, judge, statsCollector);

        var result = await node.ExecuteAsync(NewStateFor(jobPosting));

        Assert.Equal(0.0, result.Updates[ScoreMatchNode.MatchConfidenceStateKey]);
        Assert.False((bool)result.Updates[ScoreMatchNode.StageOnePassedStateKey]);
        Assert.False(result.Updates.ContainsKey(ScoreMatchNode.MatchJudgmentReasoningStateKey));

        Assert.Equal(1, statsCollector.BuildReport(DateTimeOffset.UtcNow).RejectedStageOne);
    }

    [Fact]
    public async Task ExecuteAsync_StageOnePasses_CallsJudgeAndWritesMappedConfidence()
    {
        var jobPosting = new JobPosting(
            "Backend Engineer", "Acme", "Mid", new List<string> { "C#" }, "desc", "https://x.example", "https://x.example/apply", ApplyChannels.ExternalPlatform);

        var judge = new MatchStageTwoJudge(new MockLlmClient(
            """{"category":"Strong","reasoning":"Great fit","confidence":0.88}"""));
        var statsCollector = new RunStatsCollector();
        var node = new ScoreMatchNode(Cv, judge, statsCollector);

        var result = await node.ExecuteAsync(NewStateFor(jobPosting));

        Assert.True((bool)result.Updates[ScoreMatchNode.StageOnePassedStateKey]);
        Assert.Equal(0.88, result.Updates[ScoreMatchNode.MatchConfidenceStateKey]);
        Assert.Equal("Great fit", result.Updates[ScoreMatchNode.MatchJudgmentReasoningStateKey]);

        Assert.Equal(1, statsCollector.BuildReport(DateTimeOffset.UtcNow).StageTwoBreakdown[MatchCategories.Strong]);
    }

    [Fact]
    public async Task ExecuteAsync_StageOnePasses_WeakJudgment_MapsToZeroConfidence()
    {
        var jobPosting = new JobPosting(
            "Backend Engineer", "Acme", "Mid", new List<string> { "C#" }, "desc", "https://x.example", "https://x.example/apply", ApplyChannels.ExternalPlatform);

        var judge = new MatchStageTwoJudge(new MockLlmClient(
            """{"category":"Weak","reasoning":"Not a fit","confidence":0.9}"""));
        var node = new ScoreMatchNode(Cv, judge, new RunStatsCollector());

        var result = await node.ExecuteAsync(NewStateFor(jobPosting));

        Assert.True((bool)result.Updates[ScoreMatchNode.StageOnePassedStateKey]); // stage one still passed
        Assert.Equal(0.0, result.Updates[ScoreMatchNode.MatchConfidenceStateKey]); // but mapped to 0
    }

    [Fact]
    public async Task ExecuteAsync_NoJobPostingInState_Throws()
    {
        var judge = new MatchStageTwoJudge(new MockLlmClient("irrelevant"));
        var node = new ScoreMatchNode(Cv, judge, new RunStatsCollector());

        await Assert.ThrowsAsync<InvalidOperationException>(() => node.ExecuteAsync(new GraphState()));
    }
}
