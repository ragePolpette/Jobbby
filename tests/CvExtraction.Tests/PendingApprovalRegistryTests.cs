using Notifications;
using Xunit;

namespace CvExtraction.Tests;

public class PendingApprovalRegistryTests
{
    [Fact]
    public async Task WaitForReplyAsync_UnblocksWhenOnReplyArrivesForTheSameMessageId()
    {
        var registry = new PendingApprovalRegistry();

        var waitTask = registry.WaitForReplyAsync(42, TimeSpan.FromSeconds(5));

        registry.OnReply(42, "yes, approved");

        Assert.Equal("yes, approved", await waitTask);
    }

    [Fact]
    public async Task WaitForReplyAsync_IgnoresReplyForADifferentMessageId()
    {
        var registry = new PendingApprovalRegistry();

        var waitTask = registry.WaitForReplyAsync(1, TimeSpan.FromMilliseconds(200));

        registry.OnReply(999, "not for you");

        Assert.Equal(PendingApprovalRegistry.TimeoutSentinel, await waitTask);
    }

    [Fact]
    public async Task WaitForReplyAsync_ReturnsSentinelOnTimeout_WithoutThrowing()
    {
        var registry = new PendingApprovalRegistry();

        var result = await registry.WaitForReplyAsync(7, TimeSpan.FromMilliseconds(50));

        Assert.Equal(PendingApprovalRegistry.TimeoutSentinel, result);
    }

    [Fact]
    public async Task WaitForReplyAsync_ViaMockGateway_UnblocksOnSimulatedReply()
    {
        var gateway = new MockTelegramGateway();
        var registry = new PendingApprovalRegistry();
        gateway.ReplyReceived += registry.OnReply;

        var messageId = await gateway.SendAsync("Approve this?");
        var waitTask = registry.WaitForReplyAsync(messageId, TimeSpan.FromSeconds(5));

        gateway.SimulateReply(messageId, "approved");

        Assert.Equal("approved", await waitTask);
    }
}
