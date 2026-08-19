namespace GraphEngine;

/// <summary>
/// Immutable configuration of a graph: its nodes, their outgoing edges, and the
/// max_steps safety limit. A <see cref="GraphDefinition"/> carries no state tied to any
/// single execution, so the same instance can be shared and run concurrently — call
/// <see cref="CreateRun"/> once per execution to get an isolated <see cref="GraphRun"/>.
/// </summary>
public sealed class GraphDefinition
{
    private readonly Dictionary<string, INode> _nodes = new();
    private readonly Dictionary<string, Edge> _edges = new();

    public const string End = Edge.End;

    public int MaxSteps { get; }

    public GraphDefinition(int maxSteps = 100)
    {
        if (maxSteps <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxSteps), "max_steps must be greater than zero.");

        MaxSteps = maxSteps;
    }

    public GraphDefinition RegisterNode(string name, INode node)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Node name must not be empty.", nameof(name));

        _nodes[name] = node ?? throw new ArgumentNullException(nameof(node));
        return this;
    }

    public GraphDefinition RegisterEdge(Edge edge)
    {
        ArgumentNullException.ThrowIfNull(edge);
        _edges[edge.From] = edge;
        return this;
    }

    public GraphDefinition RegisterEdge(string fromNode, Func<GraphState, string> router) =>
        RegisterEdge(new Edge(fromNode, router));

    /// <summary>Creates a new, independently steerable execution of this graph.</summary>
    public GraphRun CreateRun() => new(this);

    internal bool TryGetNode(string name, out INode node) => _nodes.TryGetValue(name, out node!);

    internal bool TryGetEdge(string name, out Edge edge) => _edges.TryGetValue(name, out edge!);

    internal bool ContainsNode(string name) => _nodes.ContainsKey(name);
}
