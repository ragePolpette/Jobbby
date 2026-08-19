using System.Collections.Concurrent;

namespace GraphEngine;

/// <summary>
/// A single, independently steerable execution of a <see cref="GraphDefinition"/>.
/// Created via <see cref="GraphDefinition.CreateRun"/>; all state that is specific to one
/// execution — pending steering injections, pause/resume signalling — lives here rather
/// than on the shared definition, so multiple runs of the same graph can proceed
/// concurrently without interfering with one another.
/// </summary>
public sealed class GraphRun
{
    private readonly GraphDefinition _definition;
    private readonly ConcurrentQueue<(string Key, object Value)> _pendingInjections = new();

    private volatile bool _pauseRequested;
    private TaskCompletionSource<bool>? _pauseSignal;

    internal GraphRun(GraphDefinition definition)
    {
        _definition = definition;
    }

    /// <summary>
    /// Runtime steering hook: enqueues a key/value pair to be written into the running
    /// <see cref="GraphState"/> just before the next node executes. Safe to call from any
    /// thread while <see cref="RunAsync"/> is in flight on another task.
    /// </summary>
    public void Steer(string key, object value) => _pendingInjections.Enqueue((key, value));

    /// <summary>
    /// Requests that this run suspend before its next node executes. Combine with
    /// <see cref="Steer"/> to guarantee an injected value is in place before a specific
    /// node runs, then call <see cref="Resume"/> to let execution continue.
    /// </summary>
    public void RequestPause() => _pauseRequested = true;

    /// <summary>Resumes this run if it is currently suspended after <see cref="RequestPause"/>.</summary>
    public void Resume() => _pauseSignal?.TrySetResult(true);

    public bool IsPaused => _pauseSignal is { Task.IsCompleted: false };

    public async Task<GraphRunResult> RunAsync(
        string startNode,
        GraphState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (!_definition.ContainsNode(startNode))
            throw new InvalidOperationException($"No node registered with name '{startNode}'.");

        var current = startNode;
        var step = 0;

        while (!string.Equals(current, GraphDefinition.End, StringComparison.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (step >= _definition.MaxSteps)
                throw new GraphMaxStepsExceededException(_definition.MaxSteps);

            if (_pauseRequested)
                await WaitForResumeAsync(cancellationToken).ConfigureAwait(false);

            ApplyPendingInjections(state);

            if (!_definition.TryGetNode(current, out var node))
                throw new InvalidOperationException($"No node registered with name '{current}'.");

            if (!_definition.TryGetEdge(current, out var edge))
                throw new InvalidOperationException($"No edge registered for node '{current}'.");

            var before = state.Snapshot();
            var result = await node.ExecuteAsync(state).ConfigureAwait(false);
            state.ApplyUpdates(result.Updates);
            var after = state.Snapshot();

            var next = edge.Router(state);

            state.RecordStep(new GraphStepLog(step, current, DateTimeOffset.UtcNow, before, after, next));

            current = next;
            step++;
        }

        return new GraphRunResult(state, step, GraphRunStatus.Completed);
    }

    private async Task WaitForResumeAsync(CancellationToken cancellationToken)
    {
        _pauseRequested = false;
        _pauseSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using (cancellationToken.Register(() => _pauseSignal.TrySetCanceled(cancellationToken)))
        {
            await _pauseSignal.Task.ConfigureAwait(false);
        }

        _pauseSignal = null;
    }

    private void ApplyPendingInjections(GraphState state)
    {
        while (_pendingInjections.TryDequeue(out var item))
            state.Set(item.Key, item.Value);
    }
}
