using ApplicationLedger;
using GraphEngine;
using JobPostings;

namespace Host.Nodes;

/// <summary>Builds the ledger record of one evaluated posting from the graph state.</summary>
public static class PostingRecordFactory
{
    public static ApplicationRecord Create(GraphState state, string outcome, string reason)
    {
        var posting = state.Get<JobPosting>(NormalizeJobPostingNode.JobPostingStateKey);
        var company = posting?.Company ?? state.Get<string>(JobApplicationStateKeys.Company) ?? string.Empty;
        var title = posting?.Title ?? state.Get<string>(JobApplicationStateKeys.Title) ?? string.Empty;
        var dedupeKey = state.Get<string>(DedupeCheckNode.DedupeKeyStateKey) ?? PostingIdentity.Key(company, title);

        return new ApplicationRecord(
            dedupeKey,
            company,
            title,
            posting?.ApplyUrl ?? state.Get<string>(JobApplicationStateKeys.SourceUrl),
            DateTimeOffset.UtcNow,
            outcome,
            PostingId: PostingIdentity.Id(dedupeKey),
            Reason: reason);
    }
}
