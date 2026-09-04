using System.Globalization;
using ApplicationLedger;
using CvExtraction;
using GraphEngine;
using Host.Nodes;
using JobPostings;
using Matching;
using Notifications;
using Reporting;
using Xunit;

namespace Host.Tests;

public class HostGraphTests
{
    // Matches DefaultCandidateCv (skills incl. "C#", 4y -> Mid band) so stage one always
    // passes by default - these tests are about DedupeCheck/ScoreMatch-threshold/
    // AskApproval/RecordIfApproved routing, not about the stage-one filter itself
    // (see HighAndLowConfidence tests below for that boundary, and ScoreMatchNode's own
    // stage-one rejection test).
    private static readonly CvData DefaultCandidateCv = new()
    {
        Name = "Test Candidate",
        YearsExperience = 4,
        Seniority = "Mid",
        Roles = new List<CvRole>(),
        Skills = new List<string> { "C#", ".NET" },
        Languages = new List<string> { "English" },
    };

    private static ILlmClient NewJudgmentLlmClient(string category, double confidence, string reasoning = "test reasoning") =>
        new MockLlmClient(
            $$"""{"category":"{{category}}","reasoning":"{{reasoning}}","confidence":{{confidence.ToString(CultureInfo.InvariantCulture)}}}""");

    /// <summary>
    /// Pre-normalized state for tests that only care about DedupeCheck onward and want to
    /// skip NormalizeJobPosting: includes both the JobPosting object (read by
    /// DedupeCheckNode and ScoreMatchNode) and the flat Company/Title/SourceUrl keys
    /// (read by AskApproval/RecordIfApproved), exactly as NormalizeJobPostingNode would
    /// have left them. RequiredStack/SeniorityLevel match DefaultCandidateCv.
    /// </summary>
    private static GraphState NewApplicationState(string company = "Acme", string title = "Backend Engineer")
    {
        var jobPosting = new JobPosting(
            title, company, "Mid", new List<string> { "C#" }, "desc", "https://example.com", "https://example.com/apply", ApplyChannels.ExternalPlatform);

        return new GraphState(new Dictionary<string, object>
        {
            [NormalizeJobPostingNode.JobPostingStateKey] = jobPosting,
            [JobApplicationStateKeys.Company] = company,
            [JobApplicationStateKeys.Title] = title,
            [JobApplicationStateKeys.SourceUrl] = "https://example.com",
        });
    }

    private static string NewLedgerPath() => Path.Combine(Path.GetTempPath(), $"applications-{Guid.NewGuid():N}.json");

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
            var dedupeKey = DedupeKey.Normalize("Acme", "Backend Engineer");
            ledger.RecordApplied(new ApplicationRecord(dedupeKey, "Acme", "Backend Engineer", null, DateTimeOffset.UtcNow, ApplicationOutcomes.Applied));

            var llmClient = NewJudgmentLlmClient(MatchCategories.Strong, 0.9); // never called, dedupe hits first
            var definition = HostGraph.Build(gateway, registry, ledger, llmClient, DefaultCandidateCv, new RunStatsCollector());
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
    public async Task StageOneRejected_RoutesStraightToEnd_NeverReachesTelegram()
    {
        var gateway = new MockTelegramGateway();
        var registry = new PendingApprovalRegistry();
        gateway.ReplyReceived += registry.OnReply;

        var ledgerPath = NewLedgerPath();
        try
        {
            var ledger = new ApplicationLedger.ApplicationLedger(ledgerPath);
            // Would blow up if actually parsed as a judgment - proves stage two never runs.
            var llmClient = new MockLlmClient("not valid judgment json");
            var definition = HostGraph.Build(gateway, registry, ledger, llmClient, DefaultCandidateCv, new RunStatsCollector());

            // No overlap with DefaultCandidateCv's skills (C#, .NET).
            var jobPosting = new JobPosting(
                "Rust Engineer", "Acme", "Mid", new List<string> { "Rust" }, "desc",
                "https://example.com", "https://example.com/apply", ApplyChannels.ExternalPlatform);

            var state = new GraphState(new Dictionary<string, object>
            {
                [NormalizeJobPostingNode.JobPostingStateKey] = jobPosting,
                [JobApplicationStateKeys.Company] = "Acme",
                [JobApplicationStateKeys.Title] = "Rust Engineer",
                [JobApplicationStateKeys.SourceUrl] = "https://example.com",
            });

            await definition.CreateRun().RunAsync(HostGraph.DedupeCheckNodeName, state);

            Assert.False(state.Get<bool>(ScoreMatchNode.StageOnePassedStateKey));
            Assert.Equal(0.0, state.Get<double>(ScoreMatchNode.MatchConfidenceStateKey));
            Assert.False(state.ContainsKey(ScoreMatchNode.MatchJudgmentReasoningStateKey)); // stage two never ran
            Assert.Empty(gateway.SentMessages);
            Assert.False(state.Get<bool>(RecordIfApprovedNode.RecordedStateKey));
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
            var llmClient = NewJudgmentLlmClient(MatchCategories.Borderline, 0.3);
            var definition = HostGraph.Build(
                gateway, registry, ledger, llmClient, DefaultCandidateCv, new RunStatsCollector(), approvalTimeout: TimeSpan.FromSeconds(5));
            var state = NewApplicationState();

            var runTask = definition.CreateRun().RunAsync(HostGraph.DedupeCheckNodeName, state);
            gateway.SimulateReply(1, "  Sì  "); // exercises trim + case/accent-insensitive matching

            await runTask;

            Assert.Contains("Acme", Assert.Single(gateway.SentMessages));
            Assert.True(state.Get<bool>(RecordIfApprovedNode.RecordedStateKey));
            Assert.Equal(ApplicationOutcomes.Applied, state.Get<string>(RecordIfApprovedNode.OutcomeStateKey));

            var dedupeKey = DedupeKey.Normalize("Acme", "Backend Engineer");
            Assert.True(ledger.HasBeenProcessed(dedupeKey));
        }
        finally
        {
            File.Delete(ledgerPath);
        }
    }

    [Fact]
    public async Task Rejected_RecordsRejectionOutcomeSoItIsNotReproposedLater()
    {
        var gateway = new MockTelegramGateway();
        var registry = new PendingApprovalRegistry();
        gateway.ReplyReceived += registry.OnReply;

        var ledgerPath = NewLedgerPath();
        try
        {
            var ledger = new ApplicationLedger.ApplicationLedger(ledgerPath);
            var llmClient = NewJudgmentLlmClient(MatchCategories.Borderline, 0.3);
            var definition = HostGraph.Build(
                gateway, registry, ledger, llmClient, DefaultCandidateCv, new RunStatsCollector(), approvalTimeout: TimeSpan.FromSeconds(5));
            var state = NewApplicationState();

            var runTask = definition.CreateRun().RunAsync(HostGraph.DedupeCheckNodeName, state);
            gateway.SimulateReply(1, "no grazie");

            await runTask;

            // Not "applied", but IS now recorded - a rejection is a terminal outcome too.
            Assert.False(state.Get<bool>(RecordIfApprovedNode.RecordedStateKey));
            Assert.Equal(ApplicationOutcomes.Rejected, state.Get<string>(RecordIfApprovedNode.OutcomeStateKey));

            var dedupeKey = DedupeKey.Normalize("Acme", "Backend Engineer");
            Assert.True(ledger.HasBeenProcessed(dedupeKey));
        }
        finally
        {
            File.Delete(ledgerPath);
        }
    }

    [Fact]
    public async Task Rejected_ResponseContainingSiAsSubstring_IsNotMisreadAsApproval_ButIsRecordedAsRejected()
    {
        var gateway = new MockTelegramGateway();
        var registry = new PendingApprovalRegistry();
        gateway.ReplyReceived += registry.OnReply;

        var ledgerPath = NewLedgerPath();
        try
        {
            var ledger = new ApplicationLedger.ApplicationLedger(ledgerPath);
            var llmClient = NewJudgmentLlmClient(MatchCategories.Borderline, 0.3);
            var definition = HostGraph.Build(
                gateway, registry, ledger, llmClient, DefaultCandidateCv, new RunStatsCollector(), approvalTimeout: TimeSpan.FromSeconds(5));
            var state = NewApplicationState();

            var runTask = definition.CreateRun().RunAsync(HostGraph.DedupeCheckNodeName, state);
            gateway.SimulateReply(1, "no, non sono sicuro");

            await runTask;

            Assert.False(state.Get<bool>(RecordIfApprovedNode.RecordedStateKey));
            Assert.Equal(ApplicationOutcomes.Rejected, state.Get<string>(RecordIfApprovedNode.OutcomeStateKey));

            // Intentional change from before: a rejection is now recorded, so
            // HasBeenProcessed is true (it used to be false when only approvals counted).
            var dedupeKey = DedupeKey.Normalize("Acme", "Backend Engineer");
            Assert.True(ledger.HasBeenProcessed(dedupeKey));
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
            var llmClient = NewJudgmentLlmClient(MatchCategories.Strong, 0.9);
            var definition = HostGraph.Build(
                gateway, registry, ledger, llmClient, DefaultCandidateCv, new RunStatsCollector(), confidenceThreshold: 0.7);
            var state = NewApplicationState();

            await definition.CreateRun().RunAsync(HostGraph.DedupeCheckNodeName, state);

            Assert.Empty(gateway.SentMessages); // AskApprovalNode never ran
            Assert.True(state.Get<bool>(RecordIfApprovedNode.AutoApprovedStateKey));
            Assert.True(state.Get<bool>(RecordIfApprovedNode.RecordedStateKey));

            var dedupeKey = DedupeKey.Normalize("Acme", "Backend Engineer");
            Assert.True(ledger.HasBeenProcessed(dedupeKey));
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
            var llmClient = NewJudgmentLlmClient(MatchCategories.Strong, 0.7);
            var definition = HostGraph.Build(
                gateway, registry, ledger, llmClient, DefaultCandidateCv, new RunStatsCollector(), confidenceThreshold: 0.7);
            var state = NewApplicationState();

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
    public async Task WeakJudgment_MapsToZeroConfidence_StillGoesThroughTelegramApproval()
    {
        var gateway = new MockTelegramGateway();
        var registry = new PendingApprovalRegistry();
        gateway.ReplyReceived += registry.OnReply;

        var ledgerPath = NewLedgerPath();
        try
        {
            var ledger = new ApplicationLedger.ApplicationLedger(ledgerPath);
            // High confidence in a Weak judgment must still map to 0, not to that confidence.
            var llmClient = NewJudgmentLlmClient(MatchCategories.Weak, 0.95);
            var definition = HostGraph.Build(
                gateway, registry, ledger, llmClient, DefaultCandidateCv, new RunStatsCollector(),
                approvalTimeout: TimeSpan.FromSeconds(5), confidenceThreshold: 0.7);
            var state = NewApplicationState();

            var runTask = definition.CreateRun().RunAsync(HostGraph.DedupeCheckNodeName, state);
            gateway.SimulateReply(1, "  Sì  ");

            await runTask;

            Assert.Equal(0.0, state.Get<double>(ScoreMatchNode.MatchConfidenceStateKey));
            Assert.Contains("Acme", Assert.Single(gateway.SentMessages));
            Assert.False(state.Get<bool>(RecordIfApprovedNode.AutoApprovedStateKey));
            Assert.True(state.Get<bool>(RecordIfApprovedNode.RecordedStateKey));

            var dedupeKey = DedupeKey.Normalize("Acme", "Backend Engineer");
            Assert.True(ledger.HasBeenProcessed(dedupeKey));
        }
        finally
        {
            File.Delete(ledgerPath);
        }
    }

    [Fact]
    public async Task LowConfidence_TimeoutRecordsTimedOutOutcome_WithoutAutoApproving()
    {
        var gateway = new MockTelegramGateway();
        var registry = new PendingApprovalRegistry();
        gateway.ReplyReceived += registry.OnReply;

        var ledgerPath = NewLedgerPath();
        try
        {
            var ledger = new ApplicationLedger.ApplicationLedger(ledgerPath);
            var llmClient = NewJudgmentLlmClient(MatchCategories.Borderline, 0.1);
            var definition = HostGraph.Build(
                gateway, registry, ledger, llmClient, DefaultCandidateCv, new RunStatsCollector(),
                approvalTimeout: TimeSpan.FromMilliseconds(50), confidenceThreshold: 0.7);
            var state = NewApplicationState();

            await definition.CreateRun().RunAsync(HostGraph.DedupeCheckNodeName, state);

            Assert.Equal(HumanInputNode.SkippedNoResponseOutcome, state.Get<string>(AskApprovalNode.OutcomeStateKey));
            Assert.False(state.Get<bool>(RecordIfApprovedNode.AutoApprovedStateKey));
            Assert.False(state.Get<bool>(RecordIfApprovedNode.RecordedStateKey));
            Assert.Equal(ApplicationOutcomes.TimedOut, state.Get<string>(RecordIfApprovedNode.OutcomeStateKey));

            // A timeout is a terminal outcome too, recorded so it isn't re-proposed forever.
            var dedupeKey = DedupeKey.Normalize("Acme", "Backend Engineer");
            Assert.True(ledger.HasBeenProcessed(dedupeKey));
        }
        finally
        {
            File.Delete(ledgerPath);
        }
    }

    [Fact]
    public async Task FullPipeline_FromRawPosting_NormalizesThenMatchesThenAutoApproves()
    {
        var gateway = new MockTelegramGateway();
        var registry = new PendingApprovalRegistry();
        gateway.ReplyReceived += registry.OnReply;

        var ledgerPath = NewLedgerPath();
        try
        {
            var ledger = new ApplicationLedger.ApplicationLedger(ledgerPath);

            // First CompleteAsync call is NormalizeJobPosting's extraction, second is
            // ScoreMatch's stage-two judge - in that order.
            const string extractionJson = """{"company":"Acme Corp","seniorityLevel":"Mid","requiredStack":["C#",".NET"]}""";
            const string judgmentJson = """{"category":"Strong","reasoning":"Great overlap","confidence":0.95}""";
            var llmClient = new MockLlmClient(new[] { extractionJson, judgmentJson });

            var definition = HostGraph.Build(
                gateway, registry, ledger, llmClient, DefaultCandidateCv, new RunStatsCollector(), confidenceThreshold: 0.7);

            var rawPosting = new RawPosting(
                "Backend Engineer", "Ottima opportunita in C#", "https://apply.example/jobs/1", "apply.example");

            var state = new GraphState(new Dictionary<string, object>
            {
                [NormalizeJobPostingNode.RawPostingStateKey] = rawPosting,
                [JobApplicationStateKeys.SourceUrl] = "https://apply.example",
            });

            await definition.CreateRun().RunAsync(HostGraph.NormalizeJobPostingNodeName, state);

            var jobPosting = state.Get<JobPosting>(NormalizeJobPostingNode.JobPostingStateKey);
            Assert.NotNull(jobPosting);
            Assert.Equal("Acme Corp", jobPosting!.Company);
            Assert.Equal(ApplyChannels.NativeForm, jobPosting.ApplyChannel); // ApplyUrl and SourceDomain match

            Assert.True(state.Get<bool>(ScoreMatchNode.StageOnePassedStateKey));
            Assert.Empty(gateway.SentMessages); // auto-approved, never asked
            Assert.True(state.Get<bool>(RecordIfApprovedNode.AutoApprovedStateKey));
            Assert.True(state.Get<bool>(RecordIfApprovedNode.RecordedStateKey));

            var dedupeKey = DedupeKey.Normalize("Acme Corp", "Backend Engineer");
            Assert.True(ledger.HasBeenProcessed(dedupeKey));
        }
        finally
        {
            File.Delete(ledgerPath);
        }
    }

    [Fact]
    public async Task AfterRejection_SamePostingInASecondRun_IsDiscardedByDedupeCheck()
    {
        var gateway = new MockTelegramGateway();
        var registry = new PendingApprovalRegistry();
        gateway.ReplyReceived += registry.OnReply;

        var ledgerPath = NewLedgerPath();
        try
        {
            var ledger = new ApplicationLedger.ApplicationLedger(ledgerPath);
            var llmClient = NewJudgmentLlmClient(MatchCategories.Borderline, 0.3);
            var definition = HostGraph.Build(
                gateway, registry, ledger, llmClient, DefaultCandidateCv, new RunStatsCollector(), approvalTimeout: TimeSpan.FromSeconds(5));

            // First run: a human rejects it via Telegram.
            var firstState = NewApplicationState();
            var firstRunTask = definition.CreateRun().RunAsync(HostGraph.DedupeCheckNodeName, firstState);
            gateway.SimulateReply(1, "no grazie");
            await firstRunTask;

            Assert.Equal(ApplicationOutcomes.Rejected, firstState.Get<string>(RecordIfApprovedNode.OutcomeStateKey));

            // Second run: the exact same posting (same Company/Title) comes around again.
            var secondState = NewApplicationState();
            var secondResult = await definition.CreateRun().RunAsync(HostGraph.DedupeCheckNodeName, secondState);

            Assert.Equal(1, secondResult.StepsExecuted); // DedupeCheck only, then END
            Assert.True(secondState.Get<bool>(DedupeCheckNode.AlreadyAppliedStateKey));
            Assert.Single(gateway.SentMessages); // only the first run's message - never asked again
        }
        finally
        {
            File.Delete(ledgerPath);
        }
    }
}
