using Xunit;

namespace Reporting.Tests;

public class RunReportStoreTests
{
    private static RunReport NewReport(DateTimeOffset runAt, int totalFetched = 1) => new(
        RunAt: runAt,
        TotalFetched: totalFetched,
        SkippedDuplicate: 0,
        RejectedStageOne: 0,
        StageTwoBreakdown: new Dictionary<string, int>(),
        AutoApproved: 0,
        HumanApproved: 0,
        HumanRejected: 0,
        TimedOut: 0,
        ErrorsPerSource: new Dictionary<string, int>());

    [Fact]
    public void AppendRunReport_MultipleCalls_KeepsEveryPriorEntry()
    {
        var path = Path.Combine(Path.GetTempPath(), $"run-reports-{Guid.NewGuid():N}.json");
        try
        {
            var first = NewReport(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), totalFetched: 5);
            var second = NewReport(new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero), totalFetched: 8);

            RunReportStore.AppendRunReport(path, first);
            RunReportStore.AppendRunReport(path, second);

            var reports = RunReportStore.LoadRunReports(path);

            Assert.Equal(2, reports.Count);
            Assert.Equal(5, reports[0].TotalFetched);
            Assert.Equal(8, reports[1].TotalFetched);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadRunReports_NoExistingFile_ReturnsEmptyList()
    {
        var path = Path.Combine(Path.GetTempPath(), $"run-reports-{Guid.NewGuid():N}.json");
        File.Delete(path); // guarantee it does not exist

        Assert.Empty(RunReportStore.LoadRunReports(path));
    }

    [Fact]
    public void SaveCursors_OverwritesPreviousCursorForTheSameSource()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cursors-{Guid.NewGuid():N}.json");
        try
        {
            var original = new SourceCursor("Adzuna", "https://apply.example/1", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            RunReportStore.SaveCursors(path, new[] { original });

            var updated = new SourceCursor("Adzuna", "https://apply.example/2", new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero));
            RunReportStore.SaveCursors(path, new[] { updated });

            var cursors = RunReportStore.LoadCursors(path);

            var cursor = Assert.Single(cursors.Values);
            Assert.Equal("https://apply.example/2", cursor.LastSeenIdentifier); // old value is gone, not merged
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SaveCursors_DifferentSources_AllCoexist()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cursors-{Guid.NewGuid():N}.json");
        try
        {
            var cursorA = new SourceCursor("Adzuna", "id-a", DateTimeOffset.UtcNow);
            var cursorB = new SourceCursor("OtherSource", "id-b", DateTimeOffset.UtcNow);

            RunReportStore.SaveCursors(path, new[] { cursorA, cursorB });

            var cursors = RunReportStore.LoadCursors(path);

            Assert.Equal(2, cursors.Count);
            Assert.Equal("id-a", cursors["Adzuna"].LastSeenIdentifier);
            Assert.Equal("id-b", cursors["OtherSource"].LastSeenIdentifier);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadCursors_NoExistingFile_ReturnsEmpty()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cursors-{Guid.NewGuid():N}.json");
        File.Delete(path); // guarantee it does not exist

        Assert.Empty(RunReportStore.LoadCursors(path));
    }
}
