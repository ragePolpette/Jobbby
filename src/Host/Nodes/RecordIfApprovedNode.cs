using ApplicationLedger;
using GraphEngine;

namespace Host.Nodes;

/// <summary>
/// Records the application only if the approval response contains "si" (case
/// insensitive) - a deliberately simple check, not a full yes/no parser. A missing
/// response (timeout) or any other reply is treated as not approved: nothing is
/// recorded, but nothing fails either.
/// </summary>
public sealed class RecordIfApprovedNode : INode
{
    public const string RecordedStateKey = "ApplicationRecorded";

    private readonly ApplicationLedger.ApplicationLedger _ledger;

    public RecordIfApprovedNode(ApplicationLedger.ApplicationLedger ledger)
    {
        _ledger = ledger;
    }

    public Task<NodeResult> ExecuteAsync(GraphState state)
    {
        var response = state.Get<string>(AskApprovalNode.ResponseStateKey) ?? string.Empty;
        var approved = response.Contains("si", StringComparison.OrdinalIgnoreCase);

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
