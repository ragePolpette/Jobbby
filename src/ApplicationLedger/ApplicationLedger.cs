using System.Text.Json;

namespace ApplicationLedger;

/// <summary>
/// Every outcome of every posting, persisted to a single JSON file and rewritten atomically
/// on each change (the expected volume is low). Records are appended, never edited: a
/// posting's current state is its latest record, its history is all of them. On load, keys
/// written by older versions are recomputed with <see cref="PostingIdentity"/>, so dedupe
/// keeps working across the upgrade. One instance per process: it holds the file in memory.
/// </summary>
public sealed class ApplicationLedger
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string _filePath;
    private readonly List<ApplicationRecord> _records;
    private readonly object _lock = new();

    public ApplicationLedger(string filePath, IEnumerable<string>? extraCompanySuffixes = null)
    {
        _filePath = filePath;
        var suffixes = extraCompanySuffixes?.ToList();
        var stored = File.Exists(filePath)
            ? JsonSerializer.Deserialize<List<ApplicationRecord>>(File.ReadAllText(filePath)) ?? new List<ApplicationRecord>()
            : new List<ApplicationRecord>();
        _records = stored.Select(record => Rekey(record, suffixes)).ToList();
    }

    /// <summary>True if the posting's key has any outcome that keeps it from being proposed again.</summary>
    public bool HasBeenProcessed(string dedupeKey)
    {
        lock (_lock)
        {
            return _records.Any(r => r.DedupeKey == dedupeKey && ApplicationOutcomes.IsTerminal(r.Outcome));
        }
    }

    /// <summary>The posting's current state: its most recent record, or null.</summary>
    public ApplicationRecord? Current(string postingId)
    {
        lock (_lock)
        {
            return _records.Where(r => r.PostingId == postingId).OrderBy(r => r.RecordedAt).LastOrDefault();
        }
    }

    /// <summary>Every record of the posting, oldest first.</summary>
    public IReadOnlyList<ApplicationRecord> History(string postingId)
    {
        lock (_lock)
        {
            return _records.Where(r => r.PostingId == postingId).OrderBy(r => r.RecordedAt).ToList();
        }
    }

    /// <summary>Postings whose current state is Pending, most recent first.</summary>
    public IReadOnlyList<ApplicationRecord> Pending()
    {
        lock (_lock)
        {
            return _records
                .GroupBy(r => r.PostingId)
                .Select(group => group.OrderBy(r => r.RecordedAt).Last())
                .Where(r => r.Outcome == ApplicationOutcomes.Pending)
                .OrderByDescending(r => r.RecordedAt)
                .ToList();
        }
    }

    /// <summary>
    /// Records an outcome and rewrites the file. Guarded by a lock because a single ledger
    /// instance is shared across concurrent <c>GraphRun</c>s.
    /// </summary>
    public void RecordOutcome(ApplicationRecord record)
    {
        lock (_lock)
        {
            _records.Add(record.PostingId is null ? record with { PostingId = PostingIdentity.Id(record.DedupeKey) } : record);
            Config.AtomicFile.WriteAllText(_filePath, JsonSerializer.Serialize(_records, SerializerOptions));
        }
    }

    /// <summary>
    /// A user's decision on a posting: appends a copy of its current record with the new
    /// outcome, so the posting keeps all its data and its history. Null if the id is unknown.
    /// </summary>
    public ApplicationRecord? Decide(string postingId, string outcome, string reason)
    {
        lock (_lock)
        {
            var current = Current(postingId);
            if (current is null)
                return null;

            var decided = current with { Outcome = outcome, Reason = reason, RecordedAt = DateTimeOffset.UtcNow };
            RecordOutcome(decided);
            return decided;
        }
    }

    private static ApplicationRecord Rekey(ApplicationRecord record, IReadOnlyList<string>? suffixes)
    {
        var key = PostingIdentity.Key(record.Company, record.Title, suffixes, record.ApplyUrl ?? record.SourceUrl);
        return record with { DedupeKey = key, PostingId = PostingIdentity.Id(key) };
    }
}
