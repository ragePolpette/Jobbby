namespace JobPostings;

/// <summary>
/// What a job source hands back before normalization: unprocessed title/description text
/// plus enough context (SourceDomain) to later decide, deterministically, how a candidate
/// should apply.
/// </summary>
public sealed record RawPosting(
    string RawTitle,
    string RawDescription,
    string ApplyUrl,
    string SourceDomain);
