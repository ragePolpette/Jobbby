using GraphEngine;
using Host.Nodes;
using Matching;
using Notifications;
using Reporting;

namespace Host;

/// <summary>Human-readable (Italian UI) texts for a run's outcomes and summary.</summary>
public static class RunSummaryText
{
    public static string DescribeOutcome(GraphState state)
    {
        if (state.Get<bool>(DedupeCheckNode.AlreadyAppliedStateKey))
            return "scartato per dedupe";
        if (!state.Get<bool>(ScoreMatchNode.StageOnePassedStateKey))
            return $"scartato dal filtro stage 1 ({state.Get<string>(ScoreMatchNode.StageOneReasonStateKey)})";
        if (!state.ContainsKey(RecordIfApprovedNode.OutcomeStateKey))
            return $"valutato senza azioni (confidenza {state.Get<double>(ScoreMatchNode.MatchConfidenceStateKey):0.00})";
        if (state.Get<bool>(RecordIfApprovedNode.AutoApprovedStateKey))
            return "selezionato automaticamente";
        if (state.Get<string>(AskApprovalNode.OutcomeStateKey) == HumanInputNode.SkippedNoResponseOutcome)
            return "in attesa di approvazione";
        return state.Get<bool>(RecordIfApprovedNode.RecordedStateKey) ? "approvato" : "rifiutato";
    }

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
            Scartati per dedupe: {report.SkippedDuplicate}
            Scartati al filtro stage 1: {report.RejectedStageOne}
            Valutazioni stage 2: {stageTwo}
            Selezionati automaticamente: {report.AutoApproved}
            Approvati da un umano: {report.HumanApproved}
            Rifiutati da un umano: {report.HumanRejected}
            Scaduti senza risposta: {report.TimedOut}
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
