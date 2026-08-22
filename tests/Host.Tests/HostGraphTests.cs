using GraphEngine;
using Host.Nodes;
using Notifications;
using Xunit;

namespace Host.Tests;

public class HostGraphTests
{
    private static GraphState NewApplicationState(string company = "Acme", string title = "Backend Engineer") =>
        new(new Dictionary<string, object>
        {
            [JobApplicationStateKeys.Company] = company,
            [JobApplicationStateKeys.Title] = title,
            [JobApplicationStateKeys.SourceUrl] = "https://example.com",
        });

    private static string NewLedgerPath() => Path.Combine(Path.GetTempPath(), $"applications-{Guid.NewGuid():N}.json");

    private static GraphState NewApplicationStateWithConfidence(double matchConfidence, string company = "Acme", string title = "Backend Engineer")
    {
        var state = NewApplicationState(company, title);
        state.Set(ScoreMatchNode.MatchConfidenceStateKey, matchConfidence);
        return state;
    }

    [Fact]
    public async Task AlreadyApplied_RoutesStraightToEnd_WithoutAskingApproval()
    {
        var gateway = new MockTelegramGateway();
        var registry = new PendingApprovalRegistry();
        gateway.ReplyReceived += registry.OnReply;

        var ledgerPath = NewLedgerPath();
        try
        {
            var ledger = new ApplicationLedger.ApplicationLedger(ledgerPath);
            var dedupeKey = ApplicationLedger.DedupeKey.Normalize("Acme", "Backend Engineer");
            ledger.RecordApplied(new ApplicationLedger.ApplicationRecord(dedupeKey, "Acme", "Backend Engineer", null, DateTimeOffset.UtcNow));

            var definition = HostGraph.Build(gateway, registry, ledger);
            var state = NewApplicationState();

            var result = await definition.CreateRun().RunAsync(HostGraph.DedupeCheckNodeName, state);

            Assert.Equal(1, result.StepsExecuted); // DedupeCheck only, then END
            Assert.True(state.Get<bool>(DedupeCheckNode.AlreadyAppliedStateKey));
            Assert.Empty(gateway.SentMessages); // never asked for approval
        }
        finally
        {
            File.Delete(ledgerPath);
        }
    }

    [Fact]
    public async Task Approved_RecordsApplicationAndSendsThePrompt()
    {
        var gateway = new MockTelegramGateway();
        var registry = new PendingApprovalRegistry();
        gateway.ReplyReceived += registry.OnReply;

        var ledgerPath = NewLedgerPath();
        try
        {
            var ledger = new ApplicationLedger.ApplicationLedger(ledgerPath);
            var definition = HostGraph.Build(gateway, registry, ledger, approvalTimeout: TimeSpan.FromSeconds(5));
            var state = NewApplicationState();

            var runTask = definition.CreateRun().RunAsync(HostGraph.DedupeCheckNodeName, state);
            gateway.SimulateReply(1, "  Sì  "); // exercises trim + case/accent-insensitive matching

            await runTask;

            Assert.Contains("Acme", Assert.Single(gateway.SentMessages));
            Assert.True(state.Get<bool>(RecordIfApprovedNode.RecordedStateKey));

            var dedupeKey = ApplicationLedger.DedupeKey.Normalize("Acme", "Backend Engineer");
            Assert.True(ledger.HasApplied(dedupeKey));
        }
        finally
        {
            File.Delete(ledgerPath);
        }
    }

    [Fact]
    public async Task Rejected_DoesNotRecordApplication()
    {
        var gateway = new MockTelegramGateway();
        var registry = new PendingApprovalRegistry();
        gateway.ReplyReceived += registry.OnReply;

        var ledgerPath = NewLedgerPath();
        try
        {
            var ledger = new ApplicationLedger.ApplicationLedger(ledgerPath);
            var definition = HostGraph.Build(gateway, registry, ledger, approvalTimeout: TimeSpan.FromSeconds(5));
            var state = NewApplicationState();

            var runTask = definition.CreateRun().RunAsync(HostGraph.DedupeCheckNodeName, state);
            gateway.SimulateReply(1, "no grazie");

            await runTask;

            Assert.False(state.Get<bool>(RecordIfApprovedNode.RecordedStateKey));

            var dedupeKey = ApplicationLedger.DedupeKey.Normalize("Acme", "Backend Engineer");
            Assert.False(ledger.HasApplied(dedupeKey));
        }
        finally
        {
            File.Delete(ledgerPath);
        }
    }

    [Fact]
    public async Task Rejected_ResponseContainingSiAsSubstring_IsNotMisreadAsApproval()
    {
        var gateway = new MockTelegramGateway();
        var registry = new PendingApprovalRegistry();
        gateway.ReplyReceived += registry.OnReply;

        var ledgerPath = NewLedgerPath();
        try
        {
            var ledger = new ApplicationLedger.ApplicationLedger(ledgerPath);
            var definition = HostGraph.Build(gateway, registry, ledger, approvalTimeout: TimeSpan.FromSeconds(5));
            var state = NewApplicationState();

            var runTask = definition.CreateRun().RunAsync(HostGraph.DedupeCheckNodeName, state);
            gateway.SimulateReply(1, "no, non sono sicuro");

            await runTask;

            Assert.False(state.Get<bool>(RecordIfApprovedNode.RecordedStateKey));

            var dedupeKey = ApplicationLedger.DedupeKey.Normalize("Acme", "Backend Engineer");
            Assert.False(ledger.HasApplied(dedupeKey));
        }
        finally
        {
            File.Delete(ledgerPath);
        }
    }

    [Fact]
    public async Task HighConfidence_AutoApproves_NeverAsksTelegramAndStillRecords()
    {
        var gateway = new MockTelegramGateway();
        var registry = new PendingApprovalRegistry();
        gateway.ReplyReceived += registry.OnReply;

        var ledgerPath = NewLedgerPath();
        try
        {
            var ledger = new ApplicationLedger.ApplicationLedger(ledgerPath);
            var definition = HostGraph.Build(gateway, registry, ledger, confidenceThreshold: 0.7);
            var state = NewApplicationStateWithConfidence(0.9);

            await definition.CreateRun().RunAsync(HostGraph.DedupeCheckNodeName, state);

            Assert.Empty(gateway.SentMessages); // AskApprovalNode never ran
            Assert.True(state.Get<bool>(RecordIfApprovedNode.AutoApprovedStateKey));
            Assert.True(state.Get<bool>(RecordIfApprovedNode.RecordedStateKey));

            var dedupeKey = ApplicationLedger.DedupeKey.Normalize("Acme", "Backend Engineer");
            Assert.True(ledger.HasApplied(dedupeKey));
        }
        finally
        {
            File.Delete(ledgerPath);
        }
    }

    [Fact]
    public async Task ConfidenceExactlyAtThreshold_CountsAsHighEnough_AutoApproves()
    {
        var gateway = new MockTelegramGateway();
        var registry = new PendingApprovalRegistry();
        gateway.ReplyReceived += registry.OnReply;

        var ledgerPath = NewLedgerPath();
        try
        {
            var ledger = new ApplicationLedger.ApplicationLedger(ledgerPath);
            var definition = HostGraph.Build(gateway, registry, ledger, confidenceThreshold: 0.7);
            var state = NewApplicationStateWithConfidence(0.7);

            await definition.CreateRun().RunAsync(HostGraph.DedupeCheckNodeName, state);

            Assert.Empty(gateway.SentMessages);
            Assert.True(state.Get<bool>(RecordIfApprovedNode.RecordedStateKey));
        }
        finally
        {
            File.Delete(ledgerPath);
        }
    }

    [Fact]
    public async Task LowConfidence_StillGoesThroughTelegramApproval_ExistingBehaviorUnchanged()
    {
        var gateway = new MockTelegramGateway();
        var registry = new PendingApprovalRegistry();
        gateway.ReplyReceived += registry.OnReply;

        var ledgerPath = NewLedgerPath();
        try
        {
            var ledger = new ApplicationLedger.ApplicationLedger(ledgerPath);
            var definition = HostGraph.Build(
                gateway, registry, ledger, approvalTimeout: TimeSpan.FromSeconds(5), confidenceThreshold: 0.7);
            var state = NewApplicationStateWithConfidence(0.3);

            var runTask = definition.CreateRun().RunAsync(HostGraph.DedupeCheckNodeName, state);
            gateway.SimulateReply(1, "  Sì  ");

            await runTask;

            Assert.Contains("Acme", Assert.Single(gateway.SentMessages));
            Assert.False(state.Get<bool>(RecordIfApprovedNode.AutoApprovedStateKey));
            Assert.True(state.Get<bool>(RecordIfApprovedNode.RecordedStateKey));

            var dedupeKey = ApplicationLedger.DedupeKey.Normalize("Acme", "Backend Engineer");
            Assert.True(ledger.HasApplied(dedupeKey));
        }
        finally
        {
            File.Delete(ledgerPath);
        }
    }

    [Fact]
    public async Task LowConfidence_TimeoutStillSkipsWithoutRecordingOrAutoApproving()
    {
        var gateway = new MockTelegramGateway();
        var registry = new PendingApprovalRegistry();
        gateway.ReplyReceived += registry.OnReply;

        var ledgerPath = NewLedgerPath();
        try
        {
            var ledger = new ApplicationLedger.ApplicationLedger(ledgerPath);
            var definition = HostGraph.Build(
                gateway, registry, ledger, approvalTimeout: TimeSpan.FromMilliseconds(50), confidenceThreshold: 0.7);
            var state = NewApplicationStateWithConfidence(0.1);

            await definition.CreateRun().RunAsync(HostGraph.DedupeCheckNodeName, state);

            Assert.Equal(HumanInputNode.SkippedNoResponseOutcome, state.Get<string>(AskApprovalNode.OutcomeStateKey));
            Assert.False(state.Get<bool>(RecordIfApprovedNode.AutoApprovedStateKey));
            Assert.False(state.Get<bool>(RecordIfApprovedNode.RecordedStateKey));
        }
        finally
        {
            File.Delete(ledgerPath);
        }
    }

    [Fact]
    public async Task NoResponseWithinTimeout_SkipsWithoutRecordingOrBlockingForever()
    {
        var gateway = new MockTelegramGateway();
        var registry = new PendingApprovalRegistry();
        gateway.ReplyReceived += registry.OnReply;

        var ledgerPath = NewLedgerPath();
        try
        {
            var ledger = new ApplicationLedger.ApplicationLedger(ledgerPath);
            var definition = HostGraph.Build(gateway, registry, ledger, approvalTimeout: TimeSpan.FromMilliseconds(50));
            var state = NewApplicationState();

            await definition.CreateRun().RunAsync(HostGraph.DedupeCheckNodeName, state);

            Assert.Equal(HumanInputNode.SkippedNoResponseOutcome, state.Get<string>(AskApprovalNode.OutcomeStateKey));
            Assert.False(state.Get<bool>(RecordIfApprovedNode.RecordedStateKey));
        }
        finally
        {
            File.Delete(ledgerPath);
        }
    }
}
