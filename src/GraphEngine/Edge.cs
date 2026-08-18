namespace GraphEngine;

/// <summary>
/// Outgoing transition for a node: after the node executes, <see cref="Router"/> inspects
/// the current <see cref="GraphState"/> and returns the name of the next node to run, or
/// <see cref="End"/> to terminate the graph. Because routing is a plain function of state,
/// a node is free to route back to a node that already ran, which is how cycles work.
/// </summary>
public sealed class Edge
{
    public const string End = "END";

    public string From { get; }
    public Func<GraphState, string> Router { get; }

    public Edge(string from, Func<GraphState, string> router)
    {
        if (string.IsNullOrWhiteSpace(from))
            throw new ArgumentException("Edge source node name must not be empty.", nameof(from));

        From = from;
        Router = router ?? throw new ArgumentNullException(nameof(router));
    }

    /// <summary>Convenience factory for an unconditional transition to a fixed next node.</summary>
    public static Edge To(string from, string next) => new(from, _ => next);
}
