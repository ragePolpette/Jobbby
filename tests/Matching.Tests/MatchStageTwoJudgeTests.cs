using System.Text.Json;
using CvExtraction;
using GraphEngine;
using JobPostings;
using Xunit;

namespace Matching.Tests;

public class MatchStageTwoJudgeTests
{
    private static readonly JobPosting Posting = new(
        "Backend Engineer", "Acme", "Mid", new List<string> { "C#" }, "desc",
        "https://x.example", "https://x.example/apply", ApplyChannels.ExternalPlatform);

    private static readonly CvData Cv = new()
    {
        Name = "Candidate",
        YearsExperience = 4,
        Seniority = "Mid",
        Roles = new List<CvRole>(),
        Skills = new List<string> { "C#" },
        Languages = new List<string>(),
    };

    [Theory]
    [InlineData(MatchCategories.Strong)]
    [InlineData(MatchCategories.Borderline)]
    [InlineData(MatchCategories.Weak)]
    public async Task JudgeAsync_ParsesCategoryReasoningAndConfidence_ForEachCategory(string category)
    {
        var llmClient = new MockLlmClient(
            $$"""{"category":"{{category}}","reasoning":"because reasons","confidence":0.42}""");
        var judge = new MatchStageTwoJudge(llmClient);

        var judgment = await judge.JudgeAsync(Posting, Cv);

        Assert.Equal(category, judgment.Category);
        Assert.Equal("because reasons", judgment.Reasoning);
        Assert.Equal(0.42, judgment.Confidence);
    }

    [Fact]
    public async Task JudgeAsync_MalformedResponse_Throws()
    {
        var llmClient = new MockLlmClient("not json at all");
        var judge = new MatchStageTwoJudge(llmClient);

        await Assert.ThrowsAsync<JsonException>(() => judge.JudgeAsync(Posting, Cv));
    }
}
