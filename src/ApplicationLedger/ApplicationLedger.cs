using System.Text.Json;

namespace ApplicationLedger;

/// <summary>
/// Tracks every job posting that has already reached a terminal outcome - applied,
/// rejected by a human, or timed out - persisted to a single JSON file. Loads existing
/// records on construction; every <see cref="RecordApplied"/> call rewrites the whole
/// file - no incremental append, the expected volume is low enough that this is simpler
/// and safer than maintaining a partial-write log.
/// </summary>
public sealed class ApplicationLedger
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _filePath;
    private readonly List<ApplicationRecord> _records;
    private readonly object _lock = new();

    public ApplicationLedger(string filePath)
    {
        _filePath = filePath;
        _records = File.Exists(filePath)
            ? JsonSerializer.Deserialize<List<ApplicationRecord>>(File.ReadAllText(filePath)) ?? new List<ApplicationRecord>()
            : new List<ApplicationRecord>();
    }

    /// <summary>
    /// True if this dedupe key has already reached ANY terminal outcome - applied,
    /// rejected, or timed out - not just a successful application. Renamed from the
    /// original HasApplied, which became misleading once rejections and timeouts started
    /// being recorded too.
    /// </summary>
    public bool HasBeenProcessed(string dedupeKey)
    {
        lock (_lock)
        {
            return _records.Any(r => r.DedupeKey == dedupeKey);
        }
    }

    /// <summary>
    /// Records a terminal outcome and rewrites the file. Guarded by a lock because a
    /// single ledger instance can be shared across concurrent <c>GraphRun</c>s (e.g. one
    /// per source), each potentially recording around the same time.
    /// </summary>
    public void RecordApplied(ApplicationRecord record)
    {
        lock (_lock)
        {
            _records.Add(record);
            File.WriteAllText(_filePath, JsonSerializer.Serialize(_records, SerializerOptions));
        }
    }
}
