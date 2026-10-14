using GraphEngine;
using Host.Nodes;
using Matching;
using Reporting;

namespace Host;

/// <summary>Human-readable (Italian UI) texts for a run's outcomes and summary.</summary>
public static class RunSummaryText
{
    public static string DescribeOutcome(GraphState state)
    {
        if (state.Get<bool>(DedupeCheckNode.AlreadyAppliedStateKey))
            return "già visto in una run precedente";
        if (state.TryGet<string>(RecordOutcomeNode.OutcomeStateKey, out var outcome))
        {
            var reason = state.Get<string>(RecordOutcomeNode.ReasonStateKey);
            return outcome switch
            {
                ApplicationLedger.ApplicationOutcomes.AutoRejected => WithReason("scartato automaticamente", reason),
                ApplicationLedger.ApplicationOutcomes.Pending => WithReason("da decidere", reason),
                ApplicationLedger.ApplicationOutcomes.Shortlisted => WithReason("selezionato automaticamente", reason),
                _ => outcome,
            };
        }

        if (!state.Get<bool>(ScoreMatchNode.StageOnePassedStateKey))
            return $"scartato dal filtro stage 1 ({state.Get<string>(ScoreMatchNode.StageOneReasonStateKey)})";
        return $"valutato senza azioni (confidenza {state.Get<double>(ScoreMatchNode.MatchConfidenceStateKey):0.00})";
    }

    /// <summary>"label (reason)", or the reason alone when it already starts with the label.</summary>
    private static string WithReason(string label, string? reason) =>
        string.IsNullOrWhiteSpace(reason) ? label
        : reason.StartsWith(label, StringComparison.OrdinalIgnoreCase) ? reason
        : $"{label} ({reason})";

    public static string Build(RunReport report)
    {
        var stageTwo = report.StageTwoBreakdown.Count == 0
            ? "nessuna"
            : string.Join(", ", report.StageTwoBreakdown.Select(kv => $"{TranslateCategory(kv.Key)}: {kv.Value}"));
        var errors = report.ErrorsPerSource.Count == 0
            ? "nessuno"
            : string.Join(", ", report.ErrorsPerSource.Select(kv => $"{kv.Key}: {kv.Value}"));

        return $"""
            Riepilogo run {report.RunAt:yyyy-MM-dd HH:mm} UTC
            Annunci trovati: {report.TotalFetched}
            Già visti: {report.SkippedDuplicate}
            Scartati al filtro stage 1: {report.RejectedStageOne}
            Valutazioni stage 2: {stageTwo}
            Selezionati automaticamente: {report.AutoApproved}
            Da decidere: {report.Pending}
            Scartati automaticamente: {report.AutoRejected}
            Errori per fonte: {errors}
            """;
    }

    private static string TranslateCategory(string category) => category switch
    {
        MatchCategories.Strong => "forte",
        MatchCategories.Borderline => "borderline",
        MatchCategories.Weak => "debole",
        _ => category,
    };
}
