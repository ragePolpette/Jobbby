using Xunit;

namespace Matching.Tests;

public class MatchConfidenceMapperTests
{
    [Theory]
    [InlineData(0.1)]
    [InlineData(0.5)]
    [InlineData(0.95)]
    public void ToMatchConfidence_Weak_AlwaysMapsToZero_RegardlessOfConfidence(double confidence)
    {
        var judgment = new MatchJudgment(MatchCategories.Weak, "not a fit", confidence);

        Assert.Equal(0.0, MatchConfidenceMapper.ToMatchConfidence(judgment));
    }

    [Theory]
    [InlineData(MatchCategories.Strong, 0.9)]
    [InlineData(MatchCategories.Borderline, 0.4)]
    public void ToMatchConfidence_StrongOrBorderline_PassesConfidenceThrough(string category, double confidence)
    {
        var judgment = new MatchJudgment(category, "reasoning", confidence);

        Assert.Equal(confidence, MatchConfidenceMapper.ToMatchConfidence(judgment));
    }

    [Fact]
    public void ToMatchConfidence_WeakCategory_IsCaseInsensitive()
    {
        var judgment = new MatchJudgment("weak", "not a fit", 0.8);

        Assert.Equal(0.0, MatchConfidenceMapper.ToMatchConfidence(judgment));
    }

    [Theory]
    [InlineData(1.5, 1.0)]
    [InlineData(-0.5, 0.0)]
    public void ToMatchConfidence_OutOfRangeConfidence_IsClamped(double rawConfidence, double expected)
    {
        var judgment = new MatchJudgment(MatchCategories.Strong, "reasoning", rawConfidence);

        Assert.Equal(expected, MatchConfidenceMapper.ToMatchConfidence(judgment));
    }
}
