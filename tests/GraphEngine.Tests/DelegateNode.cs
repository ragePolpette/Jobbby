using GraphEngine;

namespace GraphEngine.Tests;

/// <summary>Test helper: adapts a plain delegate to <see cref="INode"/> so tests don't
/// need a bespoke class per scenario.</summary>
internal sealed class DelegateNode : INode
{
    private readonly Func<GraphState, NodeResult> _execute;

    public DelegateNode(Func<GraphState, NodeResult> execute)
    {
        _execute = execute;
    }

    public Task<NodeResult> ExecuteAsync(GraphState state, CancellationToken cancellationToken = default) => Task.FromResult(_execute(state));
}
