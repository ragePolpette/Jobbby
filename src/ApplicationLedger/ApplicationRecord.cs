namespace ApplicationLedger;

/// <summary>One recorded job application.</summary>
public sealed record ApplicationRecord(
    string DedupeKey,
    string Company,
    string Title,
    string? SourceUrl,
    DateTimeOffset AppliedAt);
