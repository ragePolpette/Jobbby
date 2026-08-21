using ApplicationLedger;
using GraphEngine;

namespace Host.Nodes;

/// <summary>
/// Computes the dedup key for Company/Title and checks it against the
/// <see cref="ApplicationLedger.ApplicationLedger"/>. Its edge routes straight to END
/// when the key is already recorded, skipping approval entirely.
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
        var company = state.Get<string>(JobApplicationStateKeys.Company) ?? string.Empty;
        var title = state.Get<string>(JobApplicationStateKeys.Title) ?? string.Empty;
        var dedupeKey = DedupeKey.Normalize(company, title);

        var alreadyApplied = _ledger.HasApplied(dedupeKey);

        return Task.FromResult(NodeResult.From(new Dictionary<string, object>
        {
            [DedupeKeyStateKey] = dedupeKey,
            [AlreadyAppliedStateKey] = alreadyApplied,
        }));
    }
}
