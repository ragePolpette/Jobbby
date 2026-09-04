using JobPostings;
using Xunit;

namespace Host.Tests;

public class CursorSelectionTests
{
    [Fact]
    public void SelectMostRecent_MixedOrder_PicksThePostingWithTheLatestPostedAt()
    {
        var older = new RawPosting("Old", "desc", "https://x.example/old", "x.example", PostedAt: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var newest = new RawPosting("New", "desc", "https://x.example/new", "x.example", PostedAt: new DateTimeOffset(2026, 1, 10, 0, 0, 0, TimeSpan.Zero));
        var middle = new RawPosting("Mid", "desc", "https://x.example/mid", "x.example", PostedAt: new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero));

        // Deliberately not in chronological order - the newest one is neither first nor last.
        var postings = new[] { older, newest, middle };

        var cursor = CursorSelection.SelectMostRecent("Adzuna", postings, DateTimeOffset.UtcNow);

        Assert.Equal("https://x.example/new", cursor.LastSeenIdentifier);
        Assert.Equal("Adzuna", cursor.SourceName);
    }

    [Fact]
    public void SelectMostRecent_NoPostingHasAPostedAt_FallsBackToTheFirstPosting()
    {
        var first = new RawPosting("A", "desc", "https://x.example/a", "x.example");
        var second = new RawPosting("B", "desc", "https://x.example/b", "x.example");

        var cursor = CursorSelection.SelectMostRecent("Adzuna", new[] { first, second }, DateTimeOffset.UtcNow);

        Assert.Equal("https://x.example/a", cursor.LastSeenIdentifier);
    }

    [Fact]
    public void SelectMostRecent_SomePostingsMissingPostedAt_StillPicksTheOneThatHasTheLatestDate()
    {
        var undated = new RawPosting("Undated", "desc", "https://x.example/undated", "x.example");
        var dated = new RawPosting("Dated", "desc", "https://x.example/dated", "x.example", PostedAt: DateTimeOffset.UtcNow);

        var cursor = CursorSelection.SelectMostRecent("Adzuna", new[] { undated, dated }, DateTimeOffset.UtcNow);

        Assert.Equal("https://x.example/dated", cursor.LastSeenIdentifier);
    }
}
