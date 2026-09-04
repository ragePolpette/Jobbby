namespace ApplicationLedger;

/// <summary>
/// One recorded, terminal outcome for a job posting - not only successful applications
/// despite the type's name (kept for continuity): <see cref="Outcome"/> (see
/// <see cref="ApplicationOutcomes"/>) says whether it was actually applied to, rejected
/// by a human, or timed out waiting for one.
/// </summary>
public sealed record ApplicationRecord(
    string DedupeKey,
    string Company,
    string Title,
    string? SourceUrl,
    DateTimeOffset AppliedAt,
    string Outcome);
