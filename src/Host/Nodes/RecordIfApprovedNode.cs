using ApplicationLedger;
using GraphEngine;

namespace Host.Nodes;

/// <summary>
/// Records the application if either: the approval response is exactly "si"/"sì"
/// (trimmed, case insensitive, accent-insensitive - a whole-word match, not a substring
/// one, so replies like "no, non sono sicuro" or "così così" aren't misread as approval),
/// or <see cref="AutoApprovedStateKey"/> was set by the ScoreMatch edge because
/// confidence cleared the threshold. A missing response (timeout) and no auto-approval is
/// treated as not approved: nothing is recorded, but nothing fails either.
/// </summary>
public sealed class RecordIfApprovedNode : INode
{
    public const string RecordedStateKey = "ApplicationRecorded";

    /// <summary>Set directly on state by HostGraph's ScoreMatch edge, not by a node.</summary>
    public const string AutoApprovedStateKey = "AutoApproved";

    private readonly ApplicationLedger.ApplicationLedger _ledger;

    public RecordIfApprovedNode(ApplicationLedger.ApplicationLedger ledger)
    {
        _ledger = ledger;
    }

    public Task<NodeResult> ExecuteAsync(GraphState state)
    {
        var response = state.Get<string>(AskApprovalNode.ResponseStateKey) ?? string.Empty;
        var normalized = response.Trim().ToLowerInvariant();
        var approved = normalized is "si" or "sì" || state.Get<bool>(AutoApprovedStateKey);

        if (!approved)
            return Task.FromResult(NodeResult.From(RecordedStateKey, false));

        var company = state.Get<string>(JobApplicationStateKeys.Company) ?? string.Empty;
        var title = state.Get<string>(JobApplicationStateKeys.Title) ?? string.Empty;
        var dedupeKey = state.Get<string>(DedupeCheckNode.DedupeKeyStateKey) ?? DedupeKey.Normalize(company, title);
        var sourceUrl = state.Get<string>(JobApplicationStateKeys.SourceUrl);

        _ledger.RecordApplied(new ApplicationRecord(dedupeKey, company, title, sourceUrl, DateTimeOffset.UtcNow));

        return Task.FromResult(NodeResult.From(RecordedStateKey, true));
    }
}
