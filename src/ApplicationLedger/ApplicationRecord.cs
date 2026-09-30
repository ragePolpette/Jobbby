namespace ApplicationLedger;

/// <summary>
/// One entry of the ledger: a posting's outcome at a point in time, with everything needed
/// to decide on it and to write a message about it without reopening the run it came from.
/// A decision appends a new record; the posting's current state is its most recent record.
/// <see cref="SourceUrl"/> is the posting's own link (older versions stored the source's base
/// URL there). The optional members are absent in records written before they existed.
/// </summary>
public sealed record ApplicationRecord(
    string DedupeKey,
    string Company,
    string Title,
    string? SourceUrl,
    DateTimeOffset RecordedAt,
    string Outcome,
    string? PostingId = null,
    string? Reason = null)
{
    public string? RunId { get; init; }
    public string? SourceName { get; init; }
    public string? ApplyUrl { get; init; }
    public string? Excerpt { get; init; }
    public List<string>? RequiredSkills { get; init; }
    public List<string>? MustHaveSkills { get; init; }
    public List<string>? PreferredSkills { get; init; }
    public string? WorkMode { get; init; }
    public string? Location { get; init; }
    public decimal? SalaryMaximum { get; init; }
    public double? MinYearsExperience { get; init; }
    public string? Seniority { get; init; }
    public double? Confidence { get; init; }
    public string? Category { get; init; }
    public string? Reasoning { get; init; }
    public List<string>? MissingRequirements { get; init; }
    public List<string>? Warnings { get; init; }
    public PresentationMessage? Presentation { get; init; }
    public UserFullText? FullText { get; init; }
}
