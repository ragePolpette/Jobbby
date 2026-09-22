namespace ApplicationLedger;

public sealed record ApplicationRecord(
    string DedupeKey,
    string Company,
    string Title,
    string? SourceUrl,
    DateTimeOffset RecordedAt,
    string Outcome);
