using System.Text.Json;

namespace ApplicationLedger;

/// <summary>
/// Every outcome of every posting, persisted to a single JSON file that is always replaced
/// atomically. Records are appended, never edited: a posting's current state is its latest
/// record, its history is all of them. Outside a batch each change is written at once; inside
/// <see cref="BeginBatch"/> (a run) the file is rewritten every N records and when the batch
/// ends, while user decisions are always written at once. Lookups use an in-memory index. On
/// load, keys written by older versions are recomputed with <see cref="PostingIdentity"/>, so
/// dedupe keeps working across the upgrade. One instance per process: it holds the file in memory.
/// </summary>
public sealed class ApplicationLedger
{
    public const int DefaultBatchSize = 20;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string _filePath;
    private readonly object _lock = new();
    private List<ApplicationRecord> _records = new();
    private Dictionary<string, ApplicationRecord> _current = new(StringComparer.Ordinal);
    private HashSet<string> _terminalKeys = new(StringComparer.Ordinal);
    private int _batchDepth;
    private int _batchSize = 1;
    private int _unflushed;

    public ApplicationLedger(string filePath, IEnumerable<string>? extraCompanySuffixes = null)
    {
        _filePath = filePath;
        var suffixes = extraCompanySuffixes?.ToList();
        var stored = File.Exists(filePath)
            ? JsonSerializer.Deserialize<List<ApplicationRecord>>(File.ReadAllText(filePath)) ?? new List<ApplicationRecord>()
            : new List<ApplicationRecord>();
        Reindex(stored.Select(record => Rekey(record, suffixes)));
    }

    /// <summary>How many times the file has been written by this instance.</summary>
    public int WriteCount { get; private set; }

    /// <summary>True if the posting's key has any outcome that keeps it from being proposed again.</summary>
    public bool HasBeenProcessed(string dedupeKey)
    {
        lock (_lock)
            return _terminalKeys.Contains(dedupeKey);
    }

    /// <summary>The posting's current state: its most recent record, or null.</summary>
    public ApplicationRecord? Current(string postingId)
    {
        lock (_lock)
            return _current.GetValueOrDefault(postingId);
    }

    /// <summary>Every record of the posting, oldest first.</summary>
    public IReadOnlyList<ApplicationRecord> History(string postingId)
    {
        lock (_lock)
            return _records.Where(r => r.PostingId == postingId).OrderBy(r => r.RecordedAt).ToList();
    }

    /// <summary>Postings whose current state is Pending, most recent first.</summary>
    public IReadOnlyList<ApplicationRecord> Pending()
    {
        lock (_lock)
        {
            return _current.Values
                .Where(r => r.Outcome == ApplicationOutcomes.Pending)
                .OrderByDescending(r => r.RecordedAt)
                .ToList();
        }
    }

    /// <summary>
    /// Records an outcome. Written at once outside a batch, every <c>batchSize</c> records inside
    /// one. Guarded by a lock because a single ledger instance is shared across concurrent
    /// <c>GraphRun</c>s.
    /// </summary>
    public void RecordOutcome(ApplicationRecord record)
    {
        lock (_lock)
        {
            Append(record.PostingId is null ? record with { PostingId = PostingIdentity.Id(record.DedupeKey) } : record);
            _unflushed++;
            if (_batchDepth == 0 || _unflushed >= _batchSize)
                WriteLocked();
        }
    }

    /// <summary>
    /// Defers writes until <paramref name="batchSize"/> records are pending or the returned
    /// handle is disposed, which writes whatever is left.
    /// </summary>
    public IDisposable BeginBatch(int batchSize = DefaultBatchSize)
    {
        lock (_lock)
        {
            _batchDepth++;
            _batchSize = Math.Max(1, batchSize);
        }

        return new Batch(this);
    }

    /// <summary>Writes the records not yet on disk, if any.</summary>
    public void Flush()
    {
        lock (_lock)
        {
            if (_unflushed > 0)
                WriteLocked();
        }
    }

    /// <summary>
    /// A user's decision on a posting: appends a copy of its current record with the new
    /// outcome, so the posting keeps all its data and its history, and writes it at once.
    /// Null if the id is unknown.
    /// </summary>
    public ApplicationRecord? Decide(string postingId, string outcome, string reason)
    {
        lock (_lock)
        {
            var current = Current(postingId);
            if (current is null)
                return null;

            var decided = current with { Outcome = outcome, Reason = reason, RecordedAt = DateTimeOffset.UtcNow };
            Append(decided);
            WriteLocked();
            return decided;
        }
    }

    /// <summary>
    /// Recomputes every key for new company-suffix settings, on this same instance: a second
    /// instance would hold its own copy of the file and overwrite this one's writes.
    /// </summary>
    public void Rekey(IEnumerable<string>? extraCompanySuffixes)
    {
        var suffixes = extraCompanySuffixes?.ToList();
        lock (_lock)
            Reindex(_records.Select(record => Rekey(record, suffixes)).ToList());
    }

    private void Reindex(IEnumerable<ApplicationRecord> records)
    {
        _records = new List<ApplicationRecord>();
        _current = new Dictionary<string, ApplicationRecord>(StringComparer.Ordinal);
        _terminalKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var record in records)
            Append(record);
    }

    private void Append(ApplicationRecord record)
    {
        _records.Add(record);
        if (record.PostingId is { } postingId &&
            (!_current.TryGetValue(postingId, out var existing) || record.RecordedAt >= existing.RecordedAt))
        {
            _current[postingId] = record;
        }

        if (ApplicationOutcomes.IsTerminal(record.Outcome))
            _terminalKeys.Add(record.DedupeKey);
    }

    private void WriteLocked()
    {
        Config.AtomicFile.WriteAllText(_filePath, JsonSerializer.Serialize(_records, SerializerOptions));
        _unflushed = 0;
        WriteCount++;
    }

    private void EndBatch()
    {
        lock (_lock)
        {
            _batchDepth = Math.Max(0, _batchDepth - 1);
            if (_unflushed > 0)
                WriteLocked();
            if (_batchDepth == 0)
                _batchSize = 1;
        }
    }

    private static ApplicationRecord Rekey(ApplicationRecord record, IReadOnlyList<string>? suffixes)
    {
        var key = PostingIdentity.Key(record.SourceCompany ?? record.Company, record.Title, suffixes, record.ApplyUrl ?? record.SourceUrl, record.SourcePostingId);
        return record with { DedupeKey = key, PostingId = PostingIdentity.Id(key) };
    }

    private sealed class Batch(ApplicationLedger ledger) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                ledger.EndBatch();
        }
    }
}
