namespace JobPostings;

/// <summary>
/// What a job source hands back before normalization: unprocessed title/description text
/// plus enough context (SourceDomain) to later decide, deterministically, how a candidate
/// should apply. Company is optional - empty when a source doesn't expose it separately
/// from the free text - in which case normalization falls back to asking an LLM for it.
/// </summary>
public sealed record RawPosting(
    string RawTitle,
    string RawDescription,
    string ApplyUrl,
    string SourceDomain,
    string Company = "");
