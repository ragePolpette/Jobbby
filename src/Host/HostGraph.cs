using CvExtraction;
using GraphEngine;
using Host.Nodes;
using Matching;
using Notifications;

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

    public static GraphDefinition Build(
        ITelegramGateway gateway,
        PendingApprovalRegistry registry,
        ApplicationLedger.ApplicationLedger ledger,
        ILlmClient llmClient,
        CvData candidateCv,
        int maxSteps = 10,
        TimeSpan? approvalTimeout = null,
        double confidenceThreshold = 0.7)
    {
        var definition = new GraphDefinition(maxSteps);

        definition.RegisterNode(NormalizeJobPostingNodeName, new NormalizeJobPostingNode(llmClient));
        definition.RegisterNode(DedupeCheckNodeName, new DedupeCheckNode(ledger));
        definition.RegisterNode(ScoreMatchNodeName, new ScoreMatchNode(candidateCv, new MatchStageTwoJudge(llmClient)));
        definition.RegisterNode(AskApprovalNodeName, new AskApprovalNode(gateway, registry, approvalTimeout));
        definition.RegisterNode(RecordIfApprovedNodeName, new RecordIfApprovedNode(ledger));

        definition.RegisterEdge(NormalizeJobPostingNodeName, _ => DedupeCheckNodeName);

        definition.RegisterEdge(DedupeCheckNodeName, state =>
            state.Get<bool>(DedupeCheckNode.AlreadyAppliedStateKey) ? GraphDefinition.End : ScoreMatchNodeName);

        definition.RegisterEdge(ScoreMatchNodeName, state =>
        {
            // Stage-one rejection is a hard stop added ahead of the existing
            // threshold-based routing below (which is otherwise unchanged): it never
            // reaches AskApproval/Telegram at all.
            if (!state.Get<bool>(ScoreMatchNode.StageOnePassedStateKey))
                return GraphDefinition.End;

            var confidence = state.Get<double>(ScoreMatchNode.MatchConfidenceStateKey);

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
