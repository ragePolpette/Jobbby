namespace GraphEngine;

/// <summary>
/// A single unit of work in the graph. Nodes are task-agnostic: this engine has no
/// knowledge of what a node actually does, only how to run it and route from it.
/// </summary>
public interface INode
{
    Task<NodeResult> ExecuteAsync(GraphState state);
}
