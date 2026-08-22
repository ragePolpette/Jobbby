using GraphEngine;
using Host.Nodes;
using Xunit;

namespace Host.Tests;

public class ScoreMatchNodeTests
{
    [Fact]
    public async Task ExecuteAsync_NoConfidenceInState_DefaultsToPointFive()
    {
        var node = new ScoreMatchNode();
        var state = new GraphState();

        var result = await node.ExecuteAsync(state);

        Assert.Equal(0.5, result.Updates[ScoreMatchNode.MatchConfidenceStateKey]);
        Assert.Equal(ScoreMatchNode.DefaultConfidence, result.Updates[ScoreMatchNode.MatchConfidenceStateKey]);
    }

    [Fact]
    public async Task ExecuteAsync_ConfidenceAlreadyInState_PropagatesItUnchanged()
    {
        var node = new ScoreMatchNode();
        var state = new GraphState(new Dictionary<string, object>
        {
            [ScoreMatchNode.MatchConfidenceStateKey] = 0.83,
        });

        var result = await node.ExecuteAsync(state);

        Assert.Equal(0.83, result.Updates[ScoreMatchNode.MatchConfidenceStateKey]);
    }
}
