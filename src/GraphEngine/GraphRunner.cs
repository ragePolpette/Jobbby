using System.Collections.Concurrent;

namespace GraphEngine;

/// <summary>
/// Executes a graph of <see cref="INode"/>s connected by <see cref="Edge"/>s. Starting
/// from an initial node, it repeatedly executes the current node, merges its updates
/// into the <see cref="GraphState"/>, asks that node's edge which node to run next, and
/// repeats until routing returns <see cref="Edge.End"/> or the configured
/// <see cref="MaxSteps"/> is exceeded. Cycles are ordinary routing decisions: nothing
/// prevents an edge from sending execution back to a node that already ran.
/// </summary>
public sealed class GraphRunner
{
    private readonly Dictionary<string, INode> _nodes = new();
    private readonly Dictionary<string, Edge> _edges = new();
    private readonly ConcurrentQueue<(string Key, object Value)> _pendingInjections = new();

    private volatile bool _pauseRequested;
    private TaskCompletionSource<bool>? _pauseSignal;

    public const string End = Edge.End;

    public int MaxSteps { get; }

    public GraphRunner(int maxSteps = 100)
    {
        if (maxSteps <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxSteps), "max_steps must be greater than zero.");

        MaxSteps = maxSteps;
    }

    public GraphRunner RegisterNode(string name, INode node)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Node name must not be empty.", nameof(name));

        _nodes[name] = node ?? throw new ArgumentNullException(nameof(node));
        return this;
    }

    public GraphRunner RegisterEdge(Edge edge)
    {
        ArgumentNullException.ThrowIfNull(edge);
        _edges[edge.From] = edge;
        return this;
    }

    public GraphRunner RegisterEdge(string fromNode, Func<GraphState, string> router) =>
        RegisterEdge(new Edge(fromNode, router));

    /// <summary>
    /// Runtime steering hook: enqueues a key/value pair to be written into the running
    /// <see cref="GraphState"/> just before the next node executes. Safe to call from any
    /// thread while <see cref="RunAsync"/> is in flight on another task.
    /// </summary>
    public void Steer(string key, object value) => _pendingInjections.Enqueue((key, value));

    /// <summary>
    /// Requests that the run suspend before its next node executes. Combine with
    /// <see cref="Steer"/> to guarantee an injected value is in place before a specific
    /// node runs, then call <see cref="Resume"/> to let execution continue.
    /// </summary>
    public void RequestPause() => _pauseRequested = true;

    /// <summary>Resumes a run that is currently suspended after <see cref="RequestPause"/>.</summary>
    public void Resume() => _pauseSignal?.TrySetResult(true);

    public bool IsPaused => _pauseSignal is { Task.IsCompleted: false };

    public async Task<GraphRunResult> RunAsync(
        string startNode,
        GraphState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (!_nodes.ContainsKey(startNode))
            throw new InvalidOperationException($"No node registered with name '{startNode}'.");

        var current = startNode;
        var step = 0;

        while (!string.Equals(current, End, StringComparison.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (step >= MaxSteps)
                throw new GraphMaxStepsExceededException(MaxSteps);

            if (_pauseRequested)
                await WaitForResumeAsync(cancellationToken).ConfigureAwait(false);

            ApplyPendingInjections(state);

            if (!_nodes.TryGetValue(current, out var node))
                throw new InvalidOperationException($"No node registered with name '{current}'.");

            if (!_edges.TryGetValue(current, out var edge))
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
