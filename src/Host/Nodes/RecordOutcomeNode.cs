using ApplicationLedger;
using GraphEngine;
using JobPostings;
using Matching;
using Reporting;

namespace Host.Nodes;

/// <summary>
/// The asynchronous replacement for asking a human mid-run: turns the evaluation into an
/// outcome and, outside dry runs, records it. Only stage one and a <c>Weak</c> judgment
/// reject; confidence measures how sure the judge is, not the fit, so it never rejects.
/// Strong/Borderline below the threshold, and postings with nothing extractable, wait as
/// <see cref="ApplicationOutcomes.Pending"/> for the user to decide.
/// </summary>
public sealed class RecordOutcomeNode : INode
{
    public const string OutcomeStateKey = "Outcome";
    public const string ReasonStateKey = "OutcomeReason";
    /// <summary>The full <see cref="ApplicationRecord"/>, also in dry runs where the ledger is not written.</summary>
    public const string RecordStateKey = "OutcomeRecord";
    public const string InsufficientInformationReason = "informazioni insufficienti nell'estratto: incolla il testo completo dell'annuncio per valutarlo";

    private readonly ApplicationLedger.ApplicationLedger _ledger;
    private readonly RunStatsCollector _statsCollector;
    private readonly double _autoApproveThreshold;
    private readonly bool _dryRun;

    public RecordOutcomeNode(ApplicationLedger.ApplicationLedger ledger, RunStatsCollector statsCollector, double autoApproveThreshold, bool dryRun)
    {
        _ledger = ledger;
        _statsCollector = statsCollector;
        _autoApproveThreshold = autoApproveThreshold;
        _dryRun = dryRun;
    }

    public Task<NodeResult> ExecuteAsync(GraphState state, CancellationToken cancellationToken = default)
    {
        var (outcome, reason) = Decide(state);
        switch (outcome)
        {
            case ApplicationOutcomes.AutoRejected: _statsCollector.IncrementAutoRejected(); break;
            case ApplicationOutcomes.Pending: _statsCollector.IncrementPending(); break;
            case ApplicationOutcomes.Shortlisted: _statsCollector.IncrementAutoApproved(); break;
        }

        var record = PostingRecordFactory.Create(state, outcome, reason);
        if (!_dryRun)
            _ledger.RecordOutcome(record);

        return Task.FromResult(NodeResult.From(new Dictionary<string, object>
        {
            [OutcomeStateKey] = outcome,
            [ReasonStateKey] = reason,
            [RecordStateKey] = record,
        }));
    }

    private (string Outcome, string Reason) Decide(GraphState state)
    {
        if (!state.Get<bool>(ScoreMatchNode.StageOnePassedStateKey))
            return (ApplicationOutcomes.AutoRejected, state.Get<string>(ScoreMatchNode.StageOneReasonStateKey) ?? "Filtro stage 1 non superato.");

        if (state.Get<bool>(ScoreMatchNode.InsufficientInformationStateKey))
            return (ApplicationOutcomes.Pending, InsufficientInformationReason);

        var category = state.Get<string>(ScoreMatchNode.MatchCategoryStateKey) ?? string.Empty;
        if (string.Equals(category, MatchCategories.Weak, StringComparison.OrdinalIgnoreCase))
            return (ApplicationOutcomes.AutoRejected, "Giudizio: corrispondenza debole.");

        var confidence = state.Get<double>(ScoreMatchNode.MatchConfidenceStateKey);
        return confidence >= _autoApproveThreshold
            ? (ApplicationOutcomes.Shortlisted, $"Selezionato automaticamente (confidenza {confidence:0.00}).")
            : (ApplicationOutcomes.Pending, $"Da decidere (confidenza {confidence:0.00} sotto la soglia {_autoApproveThreshold:0.00}).");
    }
}
