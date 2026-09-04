using ApplicationLedger;
using GraphEngine;
using Notifications;
using Reporting;

namespace Host.Nodes;

/// <summary>
/// Records every terminal outcome to the ledger - not only successful applications:
/// approved (either a "si"/"sì" reply, trimmed/case/accent-insensitive whole-word match
/// so "no, non sono sicuro" isn't misread as approval, or <see cref="AutoApprovedStateKey"/>
/// set by the ScoreMatch edge), explicitly rejected, or timed out waiting for a reply.
/// Recording rejections and timeouts too - not just approvals - is what lets
/// DedupeCheckNode skip a posting a human already said no to in a later run.
/// </summary>
public sealed class RecordIfApprovedNode : INode
{
    public const string RecordedStateKey = "ApplicationRecorded";
    public const string OutcomeStateKey = "ApplicationOutcome";

    /// <summary>Set directly on state by HostGraph's ScoreMatch edge, not by a node.</summary>
    public const string AutoApprovedStateKey = "AutoApproved";

    private readonly ApplicationLedger.ApplicationLedger _ledger;
    private readonly RunStatsCollector _statsCollector;

    public RecordIfApprovedNode(ApplicationLedger.ApplicationLedger ledger, RunStatsCollector statsCollector)
    {
        _ledger = ledger;
        _statsCollector = statsCollector;
    }

    public Task<NodeResult> ExecuteAsync(GraphState state)
    {
        var autoApproved = state.Get<bool>(AutoApprovedStateKey);
        var response = state.Get<string>(AskApprovalNode.ResponseStateKey) ?? string.Empty;
        var normalized = response.Trim().ToLowerInvariant();
        var repliedYes = normalized is "si" or "sì";
        var timedOut = state.Get<string>(AskApprovalNode.OutcomeStateKey) == HumanInputNode.SkippedNoResponseOutcome;

        string outcome;
        if (autoApproved || repliedYes)
        {
            outcome = ApplicationOutcomes.Applied;
            if (autoApproved)
                _statsCollector.IncrementAutoApproved();
            else
                _statsCollector.IncrementHumanApproved();
        }
        else if (timedOut)
        {
            outcome = ApplicationOutcomes.TimedOut;
            _statsCollector.IncrementTimedOut();
        }
        else
        {
            outcome = ApplicationOutcomes.Rejected;
            _statsCollector.IncrementHumanRejected();
        }

        var company = state.Get<string>(JobApplicationStateKeys.Company) ?? string.Empty;
        var title = state.Get<string>(JobApplicationStateKeys.Title) ?? string.Empty;
        var dedupeKey = state.Get<string>(DedupeCheckNode.DedupeKeyStateKey) ?? DedupeKey.Normalize(company, title);
        var sourceUrl = state.Get<string>(JobApplicationStateKeys.SourceUrl);

        _ledger.RecordApplied(new ApplicationRecord(dedupeKey, company, title, sourceUrl, DateTimeOffset.UtcNow, outcome));

        return Task.FromResult(NodeResult.From(new Dictionary<string, object>
        {
            [RecordedStateKey] = outcome == ApplicationOutcomes.Applied,
            [OutcomeStateKey] = outcome,
        }));
    }
}
