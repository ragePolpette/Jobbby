using GraphEngine;
using Notifications;
using Xunit;

namespace CvExtraction.Tests;

public class HumanInputNodeTests
{
    [Fact]
    public async Task ExecuteAsync_OnReply_WritesResponseAndRespondedOutcome()
    {
        var gateway = new MockTelegramGateway();
        var registry = new PendingApprovalRegistry();
        gateway.ReplyReceived += registry.OnReply;

        var node = new HumanInputNode(gateway, registry, timeout: TimeSpan.FromSeconds(5));
        var state = new GraphState(new Dictionary<string, object>
        {
            [HumanInputNode.PromptStateKey] = "Approve this draft?",
        });

        var executeTask = node.ExecuteAsync(state);
        gateway.SimulateReply(1, "looks good");

        var result = await executeTask;

        Assert.Equal("Approve this draft?", Assert.Single(gateway.SentMessages));
        Assert.Equal(HumanInputNode.RespondedOutcome, result.Updates[HumanInputNode.OutcomeStateKey]);
        Assert.Equal("looks good", result.Updates[HumanInputNode.ResponseStateKey]);
    }

    [Fact]
    public async Task ExecuteAsync_NoReplyWithinTimeout_WritesSkippedOutcomeAndNoResponse()
    {
        var gateway = new MockTelegramGateway();
        var registry = new PendingApprovalRegistry();
        gateway.ReplyReceived += registry.OnReply;

        var node = new HumanInputNode(gateway, registry, timeout: TimeSpan.FromMilliseconds(50));
        var state = new GraphState(new Dictionary<string, object>
        {
            [HumanInputNode.PromptStateKey] = "Approve this draft?",
        });

        var result = await node.ExecuteAsync(state);

        Assert.Equal(HumanInputNode.SkippedNoResponseOutcome, result.Updates[HumanInputNode.OutcomeStateKey]);
        Assert.False(result.Updates.ContainsKey(HumanInputNode.ResponseStateKey));
    }
}
