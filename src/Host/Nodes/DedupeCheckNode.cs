using ApplicationLedger;
using GraphEngine;
using JobPostings;

namespace Host.Nodes;

/// <summary>
/// Computes the dedup key from the normalized JobPosting's Company/Title and checks it
/// against the <see cref="ApplicationLedger.ApplicationLedger"/>. Its edge routes
/// straight to END when the key is already recorded, skipping approval entirely.
/// </summary>
public sealed class DedupeCheckNode : INode
{
    public const string DedupeKeyStateKey = "DedupeKey";
    public const string AlreadyAppliedStateKey = "AlreadyApplied";

    private readonly ApplicationLedger.ApplicationLedger _ledger;

    public DedupeCheckNode(ApplicationLedger.ApplicationLedger ledger)
    {
        _ledger = ledger;
    }

    public Task<NodeResult> ExecuteAsync(GraphState state)
    {
        var jobPosting = state.Get<JobPosting>(NormalizeJobPostingNode.JobPostingStateKey);
        var company = jobPosting?.Company ?? string.Empty;
        var title = jobPosting?.Title ?? string.Empty;
        var dedupeKey = DedupeKey.Normalize(company, title);

        var alreadyApplied = _ledger.HasApplied(dedupeKey);

        return Task.FromResult(NodeResult.From(new Dictionary<string, object>
        {
            [DedupeKeyStateKey] = dedupeKey,
            [AlreadyAppliedStateKey] = alreadyApplied,
        }));
    }
}
