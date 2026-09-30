using JobPostings;
using Xunit;

namespace Host.Tests;

public class RemoteKeywordFilterTests
{
    [Theory]
    [InlineData("Pflegefachkraft (Homeoffice möglich)", "", true)]
    [InlineData("Infermiere", "Lavoro DA REMOTO, sede Bari", true)]
    [InlineData("Infirmière à distance", "", true)]
    [InlineData("Magazziniere", "turni in sede", false)]
    public void Matches_TitleOrExcerpt_IgnoringCaseAndAccents(string title, string excerpt, bool expected)
    {
        var filter = new RemoteKeywordFilter(new[] { "homeoffice", "da remoto", "a distance" });

        Assert.Equal(expected, filter.Matches(new RawPosting(title, excerpt, "https://x/1", "x")));
    }

    [Fact]
    public void AccentedKeyword_MatchesUnaccentedText()
    {
        var filter = new RemoteKeywordFilter(new[] { "télétravail" });

        Assert.True(filter.Matches(new RawPosting("Comptable", "teletravail possible", "https://x/1", "x")));
    }

    [Fact]
    public void BlankKeywordsOnly_IsEmpty_AndMatchesNothing()
    {
        var filter = new RemoteKeywordFilter(new[] { " ", "" });

        Assert.True(filter.IsEmpty);
        Assert.False(filter.Matches(new RawPosting("anything", "at all", "https://x/1", "x")));
    }
}
