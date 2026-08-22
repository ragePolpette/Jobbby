namespace Discovery;

/// <summary>A candidate found and evaluated by <see cref="DiscoveryEngine"/>.</summary>
public sealed record DiscoveryCandidate(
    string Name,
    string Url,
    string EvaluationSummary,
    int? ReliabilityScore);
