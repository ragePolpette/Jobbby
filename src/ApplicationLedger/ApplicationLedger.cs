using System.Text.Json;

namespace ApplicationLedger;

/// <summary>
/// Tracks which job postings have already been applied to, persisted to a single JSON
/// file. Loads existing records on construction; every <see cref="RecordApplied"/> call
/// rewrites the whole file - no incremental append, the expected volume is low enough
/// that this is simpler and safer than maintaining a partial-write log.
/// </summary>
public sealed class ApplicationLedger
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _filePath;
    private readonly List<ApplicationRecord> _records;

    public ApplicationLedger(string filePath)
    {
        _filePath = filePath;
        _records = File.Exists(filePath)
            ? JsonSerializer.Deserialize<List<ApplicationRecord>>(File.ReadAllText(filePath)) ?? new List<ApplicationRecord>()
            : new List<ApplicationRecord>();
    }

    public bool HasApplied(string dedupeKey) =>
        _records.Any(r => r.DedupeKey == dedupeKey);

    public void RecordApplied(ApplicationRecord record)
    {
        _records.Add(record);
        File.WriteAllText(_filePath, JsonSerializer.Serialize(_records, SerializerOptions));
    }
}
