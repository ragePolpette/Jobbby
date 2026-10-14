namespace JobPostings.Tests;

/// <summary>Completes every timer immediately and remembers the delays that were asked for.</summary>
public sealed class RecordingTimeProvider : TimeProvider
{
    private readonly List<TimeSpan> _delays = new();

    public IReadOnlyList<TimeSpan> Delays
    {
        get
        {
            lock (_delays)
                return _delays.ToList();
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        lock (_delays)
            _delays.Add(dueTime);
        ThreadPool.QueueUserWorkItem(_ => callback(state));
        return new NoopTimer();
    }

    private sealed class NoopTimer : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period) => true;

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
