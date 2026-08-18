namespace GraphEngine;

/// <summary>
/// Output produced by a node after execution. Updates are merged into the
/// <see cref="GraphState"/> by the <see cref="GraphRunner"/> after the node returns,
/// keeping node implementations free of direct state mutation.
/// </summary>
public sealed class NodeResult
{
    public static readonly NodeResult Empty = new();

    public IReadOnlyDictionary<string, object> Updates { get; }

    public NodeResult(IReadOnlyDictionary<string, object>? updates = null)
    {
        Updates = updates ?? new Dictionary<string, object>();
    }

    public static NodeResult From(string key, object value) =>
        new(new Dictionary<string, object> { [key] = value });

    public static NodeResult From(IReadOnlyDictionary<string, object> updates) =>
        new(updates);
}
