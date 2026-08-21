using GraphEngine;
using Host.Nodes;
using Notifications;

namespace Host;

/// <summary>
/// Builds the placeholder 3-node graph: DedupeCheck -> (already applied? END : AskApproval)
/// -> RecordIfApproved -> END. Factored out of Program.cs so it can be built and run
/// against a <see cref="MockTelegramGateway"/> in tests without any real Telegram/network
/// setup.
/// </summary>
public static class HostGraph
{
    public const string DedupeCheckNodeName = "DedupeCheck";
    public const string AskApprovalNodeName = "AskApproval";
    public const string RecordIfApprovedNodeName = "RecordIfApproved";

    public static GraphDefinition Build(
        ITelegramGateway gateway,
        PendingApprovalRegistry registry,
        ApplicationLedger.ApplicationLedger ledger,
        int maxSteps = 10,
        TimeSpan? approvalTimeout = null)
    {
        var definition = new GraphDefinition(maxSteps);

        definition.RegisterNode(DedupeCheckNodeName, new DedupeCheckNode(ledger));
        definition.RegisterNode(AskApprovalNodeName, new AskApprovalNode(gateway, registry, approvalTimeout));
        definition.RegisterNode(RecordIfApprovedNodeName, new RecordIfApprovedNode(ledger));

        definition.RegisterEdge(DedupeCheckNodeName, state =>
            state.Get<bool>(DedupeCheckNode.AlreadyAppliedStateKey) ? GraphDefinition.End : AskApprovalNodeName);
        definition.RegisterEdge(AskApprovalNodeName, _ => RecordIfApprovedNodeName);
        definition.RegisterEdge(RecordIfApprovedNodeName, _ => GraphDefinition.End);

        return definition;
    }
}
