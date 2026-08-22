using GraphEngine;

namespace Host.Nodes;

/// <summary>
/// Placeholder bridge node standing in for real match scoring: reads MatchConfidence
/// from state if already present (injected from outside for now), defaulting to 0.5
/// otherwise, and just propagates it back into state. Nothing else - ready to be swapped
/// out for real matching later without touching the graph wiring around it.
/// </summary>
public sealed class ScoreMatchNode : INode
{
    public const string MatchConfidenceStateKey = "MatchConfidence";
    public const double DefaultConfidence = 0.5;

    public Task<NodeResult> ExecuteAsync(GraphState state)
    {
        var confidence = state.TryGet<double>(MatchConfidenceStateKey, out var existing)
            ? existing
            : DefaultConfidence;

        return Task.FromResult(NodeResult.From(MatchConfidenceStateKey, confidence));
    }
}
