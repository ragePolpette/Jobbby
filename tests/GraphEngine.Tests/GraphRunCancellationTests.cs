using GraphEngine;
using Xunit;

namespace GraphEngine.Tests;

public class GraphRunCancellationTests
{
    [Fact]
    public async Task RunAsync_PassesCancellationTokenToNodes()
    {
        using var cts = new CancellationTokenSource();
        CancellationToken seen = default;
        var definition = new GraphDefinition();
        definition.RegisterNode("a", new TokenCapturingNode(token => seen = token));
        definition.RegisterEdge("a", _ => GraphDefinition.End);

        await definition.CreateRun().RunAsync("a", new GraphState(), cts.Token);

        Assert.Equal(cts.Token, seen);
    }

    [Fact]
    public async Task RunAsync_CancelledDuringNode_PropagatesAndDoesNotRunNextNode()
    {
        using var cts = new CancellationTokenSource();
        var nextRan = false;
        var definition = new GraphDefinition();
        definition.RegisterNode("slow", new TokenCapturingNode(_ => cts.Cancel(), delay: Timeout.InfiniteTimeSpan));
        definition.RegisterNode("next", new TokenCapturingNode(_ => nextRan = true));
        definition.RegisterEdge("slow", _ => "next");
        definition.RegisterEdge("next", _ => GraphDefinition.End);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => definition.CreateRun().RunAsync("slow", new GraphState(), cts.Token));

        Assert.False(nextRan);
    }

    private sealed class TokenCapturingNode(Action<CancellationToken> onExecute, TimeSpan? delay = null) : INode
    {
        public async Task<NodeResult> ExecuteAsync(GraphState state, CancellationToken cancellationToken = default)
        {
            onExecute(cancellationToken);
            if (delay is not null)
                await Task.Delay(delay.Value, cancellationToken);
            return NodeResult.Empty;
        }
    }
}
