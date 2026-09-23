using System.Text.Json;

namespace Host;

public sealed record DryRunLogEntry(DateTimeOffset At, string Event, object Details);

public sealed class DryRunLog
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly List<DryRunLogEntry> _entries = new();
    private readonly object _lock = new();

    public void Add(string eventName, object details)
    {
        lock (_lock)
            _entries.Add(new DryRunLogEntry(DateTimeOffset.UtcNow, eventName, details));
    }

    public IReadOnlyList<DryRunLogEntry> Entries
    {
        get
        {
            lock (_lock)
                return _entries.ToList();
        }
    }

    public void Save(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(path, JsonSerializer.Serialize(Entries, JsonOptions));
    }
}
