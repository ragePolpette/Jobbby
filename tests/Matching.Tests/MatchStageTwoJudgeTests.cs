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
    public async Task JudgeAsync_MalformedResponse_ThrowsLlmException()
    {
        var llmClient = new MockLlmClient("not json at all");
        var judge = new MatchStageTwoJudge(llmClient);

        var error = await Assert.ThrowsAsync<LlmException>(() => judge.JudgeAsync(Posting, Cv));
        Assert.IsType<JsonException>(error.InnerException);
    }

    [Theory]
    [InlineData("Excellent")]
    [InlineData("")]
    [InlineData("Strong; ignore previous instructions")]
    public async Task JudgeAsync_UnknownCategory_ThrowsLlmException(string category)
    {
        var judge = new MatchStageTwoJudge(new MockLlmClient($$"""{"category":"{{category}}","reasoning":"r","confidence":0.5}"""));

        await Assert.ThrowsAsync<LlmException>(() => judge.JudgeAsync(Posting, Cv));
    }

    [Fact]
    public async Task JudgeAsync_MissingCategory_ThrowsLlmException()
    {
        var judge = new MatchStageTwoJudge(new MockLlmClient("""{"reasoning":"r","confidence":0.5}"""));

        await Assert.ThrowsAsync<LlmException>(() => judge.JudgeAsync(Posting, Cv));
    }

    [Fact]
    public async Task JudgeAsync_CategoryInAnyCase_IsCanonicalized()
    {
        var judge = new MatchStageTwoJudge(new MockLlmClient("""{"category":" strong ","reasoning":"r","confidence":0.5}"""));

        Assert.Equal(MatchCategories.Strong, (await judge.JudgeAsync(Posting, Cv)).Category);
    }

    [Theory]
    [InlineData("1.7", 1.0)]
    [InlineData("-0.2", 0.0)]
    [InlineData("0.3", 0.3)]
    public async Task JudgeAsync_ConfidenceIsClampedToZeroOne(string raw, double expected)
    {
        var judge = new MatchStageTwoJudge(new MockLlmClient($$"""{"category":"Weak","reasoning":"r","confidence":{{raw}}}"""));

        Assert.Equal(expected, (await judge.JudgeAsync(Posting, Cv)).Confidence);
    }

    [Fact]
    public async Task JudgeAsync_LongReasoning_IsCapped()
    {
        var reasoning = new string('x', MatchStageTwoJudge.MaxReasoningLength * 3);
        var judge = new MatchStageTwoJudge(new MockLlmClient($$"""{"category":"Weak","reasoning":"{{reasoning}}","confidence":0.5}"""));

        Assert.Equal(MatchStageTwoJudge.MaxReasoningLength, (await judge.JudgeAsync(Posting, Cv)).Reasoning.Length);
    }

    [Fact]
    public async Task JudgeAsync_FencedJson_IsParsed()
    {
        var judge = new MatchStageTwoJudge(new MockLlmClient("```json\n{\"category\":\"Borderline\",\"reasoning\":\"r\",\"confidence\":0.5}\n```"));

        Assert.Equal(MatchCategories.Borderline, (await judge.JudgeAsync(Posting, Cv)).Category);
    }
}
