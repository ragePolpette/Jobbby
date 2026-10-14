using ApplicationLedger;
using Config;
using Reporting;

namespace Host;

public enum RunStatus
{
    Running,
    Completed,
    Failed,
    Interrupted,
}

public enum PostingStatus
{
    /// <summary>Went through the graph and got an outcome.</summary>
    Evaluated,

    /// <summary>Already decided in an earlier run: not evaluated again.</summary>
    Skipped,

    /// <summary>Evaluation failed (e.g. the LLM call); see <see cref="RunPosting.Error"/>.</summary>
    Failed,

    /// <summary>The run was stopped before this posting finished.</summary>
    Interrupted,
}

public sealed record QueryRecord(string Source, string Query, string Sweep, int Returned, int DroppedByPrefilter, string? Error);

/// <param name="Record">The full evaluation (the same shape the ledger stores), when there is one.</param>
/// <param name="SourceCompany">The company as the source reported it, before any LLM fallback.</param>
/// <param name="SourceId">The source's own id of the ad, when it has one.</param>
public sealed record RunPosting(
    string PostingId,
    string Title,
    string Company,
    string ApplyUrl,
    string Source,
    string Sweep,
    PostingStatus Status,
    string? Summary,
    ApplicationRecord? Record,
    string? Error,
    string? SourceCompany = null,
    string? SourceId = null);

/// <summary>
/// Everything about one run, persisted as <c>DataDir/runs/&lt;RunId&gt;.json</c> for dry and
/// normal runs alike: what was searched and how, what came back, and what happened to every
/// posting. <see cref="Mode"/> is "dry", "normal", or "legacy" for summaries imported from
/// the old <c>run-reports.json</c>.
/// </summary>
public sealed record RunRecord
{
    public string RunId { get; init; } = string.Empty;
    public string Mode { get; init; } = string.Empty;
    public RunStatus Status { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? EndedAt { get; init; }
    public RunSettingsSnapshot Settings { get; init; } = new();
    public AreaSettings? Area { get; init; }
    public List<string> Queries { get; init; } = new();
    public int AdzunaCalls { get; init; }
    public List<QueryRecord> QueryOutcomes { get; init; } = new();
    public List<RunPosting> Postings { get; init; } = new();
    public RunReport? Report { get; init; }
    public List<string> Warnings { get; init; } = new();
    public string? Error { get; init; }
}
