namespace GraphEngine;

/// <summary>
/// One executed step in a graph run, captured for observability: which node ran, when,
/// and what the state looked like immediately before and after it ran.
/// </summary>
public sealed record GraphStepLog(
    int StepNumber,
    string NodeName,
    DateTimeOffset Timestamp,
    IReadOnlyDictionary<string, object> StateBefore,
    IReadOnlyDictionary<string, object> StateAfter,
    string NextNode);

/// <summary>
/// Typed key-value bag that flows through the graph, plus the running history of every
/// step the graph has executed. This is the only thing nodes and edges see or touch.
/// </summary>
public sealed class GraphState
{
    private readonly Dictionary<string, object> _data;
    private readonly List<GraphStepLog> _history = new();

    public GraphState() : this(new Dictionary<string, object>())
    {
    }

    public GraphState(IDictionary<string, object> initialData)
    {
        _data = new Dictionary<string, object>(initialData);
    }

    public IReadOnlyList<GraphStepLog> History => _history.AsReadOnly();

    public object? this[string key]
    {
        get => _data.TryGetValue(key, out var value) ? value : null;
        set
        {
            if (value is null)
                _data.Remove(key);
            else
                _data[key] = value;
        }
    }

    public bool ContainsKey(string key) => _data.ContainsKey(key);

    public void Set(string key, object value) => _data[key] = value;

    public T? Get<T>(string key) =>
        _data.TryGetValue(key, out var value) && value is T typed ? typed : default;

    public bool TryGet<T>(string key, out T value)
    {
        if (_data.TryGetValue(key, out var raw) && raw is T typed)
        {
            value = typed;
            return true;
        }

        value = default!;
        return false;
    }

    /// <summary>Point-in-time copy of the state data, used for before/after step logging.</summary>
    public IReadOnlyDictionary<string, object> Snapshot() => new Dictionary<string, object>(_data);

    internal void ApplyUpdates(IReadOnlyDictionary<string, object> updates)
    {
        foreach (var (key, value) in updates)
            _data[key] = value;
    }

    internal void RecordStep(GraphStepLog log) => _history.Add(log);
}
