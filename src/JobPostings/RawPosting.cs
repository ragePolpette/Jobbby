namespace JobPostings;

/// <summary>
/// What a job source hands back before normalization: unprocessed title/description text
/// plus enough context (SourceDomain) to later decide, deterministically, how a candidate
/// should apply. Company is optional - empty when a source doesn't expose it separately
/// from the free text - in which case normalization falls back to asking an LLM for it.
/// PostedAt is likewise optional and purely internal bookkeeping: it isn't normalized
/// into JobPosting, it exists only so a caller can pick the truly most recent posting
/// out of a batch (e.g. for a source cursor) instead of guessing from response order.
/// </summary>
public sealed record RawPosting(
    string RawTitle,
    string RawDescription,
    string ApplyUrl,
    string SourceDomain,
    string Company = "",
    DateTimeOffset? PostedAt = null);
