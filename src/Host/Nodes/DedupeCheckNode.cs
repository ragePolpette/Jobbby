using ApplicationLedger;
using GraphEngine;
using JobPostings;
using Reporting;

namespace Host.Nodes;

/// <summary>
/// Computes the dedup key from the normalized JobPosting's Company/Title and checks it
/// against the <see cref="ApplicationLedger.ApplicationLedger"/>. Its edge routes
/// straight to END when the key has already reached a terminal outcome - applied,
/// rejected, or timed out - skipping approval entirely so a rejected posting isn't
/// re-proposed in a later run.
/// </summary>
public sealed class DedupeCheckNode : INode
{
    public const string DedupeKeyStateKey = "DedupeKey";
    public const string AlreadyAppliedStateKey = "AlreadyApplied";

    private readonly ApplicationLedger.ApplicationLedger _ledger;
    private readonly RunStatsCollector _statsCollector;

    public DedupeCheckNode(ApplicationLedger.ApplicationLedger ledger, RunStatsCollector statsCollector)
    {
        _ledger = ledger;
        _statsCollector = statsCollector;
    }

    public Task<NodeResult> ExecuteAsync(GraphState state)
    {
        var jobPosting = state.Get<JobPosting>(NormalizeJobPostingNode.JobPostingStateKey);
        var company = jobPosting?.Company ?? string.Empty;
        var title = jobPosting?.Title ?? string.Empty;
        var dedupeKey = DedupeKey.Normalize(company, title);

        var alreadyProcessed = _ledger.HasBeenProcessed(dedupeKey);

        if (alreadyProcessed)
            _statsCollector.IncrementSkippedDuplicate();

        return Task.FromResult(NodeResult.From(new Dictionary<string, object>
        {
            [DedupeKeyStateKey] = dedupeKey,
            [AlreadyAppliedStateKey] = alreadyProcessed,
        }));
    }
}
