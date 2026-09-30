using CvExtraction;
using GraphEngine;
using Host.Nodes;
using Matching;
using Notifications;
using Reporting;

namespace Host;

/// <summary>
/// Builds the placeholder graph: NormalizeJobPosting -> DedupeCheck -> (already applied?
/// END : ScoreMatch) -> (stage-one filter failed? END : confidence >= threshold?
/// auto-approve straight to RecordIfApproved : AskApproval) -> RecordIfApproved -> END.
/// Factored out of Program.cs so it can be built and run against a
/// <see cref="MockTelegramGateway"/> in tests without any real Telegram/network setup.
/// </summary>
public static class HostGraph
{
    public const string NormalizeJobPostingNodeName = "NormalizeJobPosting";
    public const string DedupeCheckNodeName = "DedupeCheck";
    public const string ScoreMatchNodeName = "ScoreMatch";
    public const string AskApprovalNodeName = "AskApproval";
    public const string RecordIfApprovedNodeName = "RecordIfApproved";
    public const string RecordOutcomeNodeName = "RecordOutcome";

    public static GraphDefinition Build(
        ITelegramGateway? gateway,
        PendingApprovalRegistry registry,
        ApplicationLedger.ApplicationLedger ledger,
        ILlmClient llmClient,
        CvData candidateCv,
        RunStatsCollector statsCollector,
        int maxSteps = 10,
        TimeSpan? approvalTimeout = null,
        double confidenceThreshold = 0.7,
        bool dryRun = false,
        StageOneCriteria? stageOneCriteria = null,
        IEnumerable<string>? extraCompanySuffixes = null)
    {
        var definition = new GraphDefinition(maxSteps);

        definition.RegisterNode(NormalizeJobPostingNodeName, new NormalizeJobPostingNode(llmClient));
        definition.RegisterNode(DedupeCheckNodeName, new DedupeCheckNode(ledger, statsCollector, extraCompanySuffixes));
        definition.RegisterNode(ScoreMatchNodeName, new ScoreMatchNode(candidateCv, new MatchStageTwoJudge(llmClient), statsCollector, stageOneCriteria));
        definition.RegisterEdge(NormalizeJobPostingNodeName, _ => DedupeCheckNodeName);
        definition.RegisterEdge(DedupeCheckNodeName, state =>
            state.Get<bool>(DedupeCheckNode.AlreadyAppliedStateKey) ? GraphDefinition.End : ScoreMatchNodeName);

        if (gateway is null)
        {
            // Asynchronous flow: every evaluated posting gets an outcome; nobody waits for a reply.
            definition.RegisterNode(RecordOutcomeNodeName, new RecordOutcomeNode(ledger, statsCollector, confidenceThreshold, dryRun));
            definition.RegisterEdge(ScoreMatchNodeName, _ => RecordOutcomeNodeName);
            definition.RegisterEdge(RecordOutcomeNodeName, _ => GraphDefinition.End);
            return definition;
        }

        // Legacy Telegram flow, kept compiled and tested but no longer wired by the runner.
        definition.RegisterNode(AskApprovalNodeName, new AskApprovalNode(gateway, registry, approvalTimeout));
        definition.RegisterNode(RecordIfApprovedNodeName, new RecordIfApprovedNode(ledger, statsCollector));

        definition.RegisterEdge(ScoreMatchNodeName, state =>
        {
            // Stage-one rejection is a hard stop: it never reaches AskApproval/Telegram at all.
            if (!state.Get<bool>(ScoreMatchNode.StageOnePassedStateKey))
                return GraphDefinition.End;

            if (dryRun)
                return GraphDefinition.End;

            // A Weak verdict never auto-approves, however confident the judge is.
            var weak = string.Equals(state.Get<string>(ScoreMatchNode.MatchCategoryStateKey), MatchCategories.Weak, StringComparison.OrdinalIgnoreCase);
            var confidence = weak ? 0.0 : state.Get<double>(ScoreMatchNode.MatchConfidenceStateKey);

            if (confidence < confidenceThreshold)
                return AskApprovalNodeName;

            // High enough confidence skips Telegram entirely - mark it and jump straight
            // to recording, same as a "si" reply would.
            state.Set(RecordIfApprovedNode.AutoApprovedStateKey, true);
            return RecordIfApprovedNodeName;
        });

        definition.RegisterEdge(AskApprovalNodeName, _ => RecordIfApprovedNodeName);
        definition.RegisterEdge(RecordIfApprovedNodeName, _ => GraphDefinition.End);

        return definition;
    }
}
