using CvExtraction;
using GraphEngine;
using Host.Nodes;
using Matching;
using Reporting;

namespace Host;

/// <summary>
/// Builds the evaluation graph: NormalizeJobPosting -> DedupeCheck -> (already decided?
/// END : ScoreMatch) -> RecordOutcome -> END. Every evaluated posting gets an outcome;
/// nobody waits for a reply mid-run.
/// </summary>
public static class HostGraph
{
    public const string NormalizeJobPostingNodeName = "NormalizeJobPosting";
    public const string DedupeCheckNodeName = "DedupeCheck";
    public const string ScoreMatchNodeName = "ScoreMatch";
    public const string RecordOutcomeNodeName = "RecordOutcome";

    public static GraphDefinition Build(
        ApplicationLedger.ApplicationLedger ledger,
        ILlmClient llmClient,
        CvData candidateCv,
        RunStatsCollector statsCollector,
        int maxSteps = 10,
        double confidenceThreshold = 0.7,
        bool dryRun = false,
        StageOneCriteria? stageOneCriteria = null,
        IEnumerable<string>? extraCompanySuffixes = null,
        PromptLanguage language = PromptLanguage.English)
    {
        var definition = new GraphDefinition(maxSteps);

        definition.RegisterNode(NormalizeJobPostingNodeName, new NormalizeJobPostingNode(llmClient, language));
        definition.RegisterNode(DedupeCheckNodeName, new DedupeCheckNode(ledger, statsCollector, extraCompanySuffixes));
        definition.RegisterNode(ScoreMatchNodeName, new ScoreMatchNode(candidateCv, new MatchStageTwoJudge(llmClient, language), statsCollector, stageOneCriteria));
        definition.RegisterNode(RecordOutcomeNodeName, new RecordOutcomeNode(ledger, statsCollector, confidenceThreshold, dryRun));

        definition.RegisterEdge(NormalizeJobPostingNodeName, _ => DedupeCheckNodeName);
        definition.RegisterEdge(DedupeCheckNodeName, state =>
            state.Get<bool>(DedupeCheckNode.AlreadyAppliedStateKey) ? GraphDefinition.End : ScoreMatchNodeName);
        definition.RegisterEdge(ScoreMatchNodeName, _ => RecordOutcomeNodeName);
        definition.RegisterEdge(RecordOutcomeNodeName, _ => GraphDefinition.End);

        return definition;
    }
}
