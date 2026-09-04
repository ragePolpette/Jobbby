using System.Collections.Concurrent;

namespace Reporting;

/// <summary>
/// Thread-safe counters for one Host run, shared across every posting's concurrent
/// GraphRun. Each Host node that reaches one of these outcomes calls the matching
/// Increment* method; <see cref="BuildReport"/> snapshots everything into a
/// <see cref="RunReport"/> once all runs have finished.
/// </summary>
public sealed class RunStatsCollector
{
    private int _totalFetched;
    private int _skippedDuplicate;
    private int _rejectedStageOne;
    private int _autoApproved;
    private int _humanApproved;
    private int _humanRejected;
    private int _timedOut;

    private readonly ConcurrentDictionary<string, int> _stageTwoBreakdown = new();
    private readonly ConcurrentDictionary<string, int> _errorsPerSource = new();

    public void IncrementTotalFetched(int count) => Interlocked.Add(ref _totalFetched, count);

    public void IncrementSkippedDuplicate() => Interlocked.Increment(ref _skippedDuplicate);

    public void IncrementRejectedStageOne() => Interlocked.Increment(ref _rejectedStageOne);

    public void IncrementStageTwoCategory(string category) =>
        _stageTwoBreakdown.AddOrUpdate(category, 1, (_, count) => count + 1);

    public void IncrementAutoApproved() => Interlocked.Increment(ref _autoApproved);

    public void IncrementHumanApproved() => Interlocked.Increment(ref _humanApproved);

    public void IncrementHumanRejected() => Interlocked.Increment(ref _humanRejected);

    public void IncrementTimedOut() => Interlocked.Increment(ref _timedOut);

    public void IncrementError(string sourceName) =>
        _errorsPerSource.AddOrUpdate(sourceName, 1, (_, count) => count + 1);

    public RunReport BuildReport(DateTimeOffset runAt) => new(
        RunAt: runAt,
        TotalFetched: Volatile.Read(ref _totalFetched),
        SkippedDuplicate: Volatile.Read(ref _skippedDuplicate),
        RejectedStageOne: Volatile.Read(ref _rejectedStageOne),
        StageTwoBreakdown: new Dictionary<string, int>(_stageTwoBreakdown),
        AutoApproved: Volatile.Read(ref _autoApproved),
        HumanApproved: Volatile.Read(ref _humanApproved),
        HumanRejected: Volatile.Read(ref _humanRejected),
        TimedOut: Volatile.Read(ref _timedOut),
        ErrorsPerSource: new Dictionary<string, int>(_errorsPerSource));
}
