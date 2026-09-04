using Xunit;

namespace Reporting.Tests;

public class RunStatsCollectorTests
{
    [Fact]
    public async Task ConcurrentIncrements_FromManyThreads_LoseNoCounts()
    {
        var collector = new RunStatsCollector();
        const int threadCount = 20;
        const int incrementsPerThread = 500;

        var tasks = Enumerable.Range(0, threadCount).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < incrementsPerThread; i++)
            {
                collector.IncrementSkippedDuplicate();
                collector.IncrementRejectedStageOne();
                collector.IncrementAutoApproved();
                collector.IncrementHumanApproved();
                collector.IncrementHumanRejected();
                collector.IncrementTimedOut();
                collector.IncrementStageTwoCategory(MatchCategories.Strong);
                collector.IncrementError("SourceA");
            }
        }));

        await Task.WhenAll(tasks);

        var report = collector.BuildReport(DateTimeOffset.UtcNow);
        const int expected = threadCount * incrementsPerThread;

        Assert.Equal(expected, report.SkippedDuplicate);
        Assert.Equal(expected, report.RejectedStageOne);
        Assert.Equal(expected, report.AutoApproved);
        Assert.Equal(expected, report.HumanApproved);
        Assert.Equal(expected, report.HumanRejected);
        Assert.Equal(expected, report.TimedOut);
        Assert.Equal(expected, report.StageTwoBreakdown[MatchCategories.Strong]);
        Assert.Equal(expected, report.ErrorsPerSource["SourceA"]);
    }

    [Fact]
    public async Task ConcurrentIncrements_AcrossDifferentSourcesAndCategories_EachTracksIndependently()
    {
        var collector = new RunStatsCollector();
        const int incrementsPerKey = 300;

        var tasks = new[]
        {
            Task.Run(() => { for (var i = 0; i < incrementsPerKey; i++) collector.IncrementError("SourceA"); }),
            Task.Run(() => { for (var i = 0; i < incrementsPerKey; i++) collector.IncrementError("SourceB"); }),
            Task.Run(() => { for (var i = 0; i < incrementsPerKey; i++) collector.IncrementStageTwoCategory(MatchCategories.Strong); }),
            Task.Run(() => { for (var i = 0; i < incrementsPerKey; i++) collector.IncrementStageTwoCategory(MatchCategories.Weak); }),
        };

        await Task.WhenAll(tasks);

        var report = collector.BuildReport(DateTimeOffset.UtcNow);

        Assert.Equal(incrementsPerKey, report.ErrorsPerSource["SourceA"]);
        Assert.Equal(incrementsPerKey, report.ErrorsPerSource["SourceB"]);
        Assert.Equal(incrementsPerKey, report.StageTwoBreakdown[MatchCategories.Strong]);
        Assert.Equal(incrementsPerKey, report.StageTwoBreakdown[MatchCategories.Weak]);
    }

    [Fact]
    public void IncrementTotalFetched_AddsTheGivenCount()
    {
        var collector = new RunStatsCollector();

        collector.IncrementTotalFetched(7);
        collector.IncrementTotalFetched(3);

        Assert.Equal(10, collector.BuildReport(DateTimeOffset.UtcNow).TotalFetched);
    }

    // Local stand-in so this test project doesn't need to reference Matching just for
    // three string constants.
    private static class MatchCategories
    {
        public const string Strong = "Strong";
        public const string Weak = "Weak";
    }
}
