using Microsoft.Extensions.Configuration;
using CvExtraction;
using GraphEngine;
using Host.Nodes;
using JobPostings;
using Notifications;
using Reporting;
using Xunit;

namespace Host.Tests;

public class DryRunTests
{
    [Fact]
    public void Options_UseSafeDefaultsAndClampLimits()
    {
        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jobbby:DryRun"] = "true",
                ["Jobbby:DryRunMaxPostingsPerSource"] = "200",
                ["Jobbby:DryRunMaxDiscoveryCandidates"] = "0",
            })
            .Build();

        var options = DryRunOptions.FromConfiguration(configuration, Path.GetTempPath());

        Assert.True(options.Enabled);
        Assert.Equal(20, options.MaxPostingsPerSource);
        Assert.Equal(1, options.MaxDiscoveryCandidates);
    }

    [Fact]
    public void Log_SavesStructuredDetailedEvents()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dry-run-{Guid.NewGuid():N}.json");
        try
        {
            var log = new DryRunLog();
            log.Add("posting_evaluated", new { Title = "Backend Engineer", ActionTaken = "none" });

            log.Save(path);

            var json = File.ReadAllText(path);
            Assert.Contains("posting_evaluated", json);
            Assert.Contains("Backend Engineer", json);
            Assert.Contains("none", json);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Graph_DryRunStopsAfterEvaluationWithoutTelegramOrLedgerWrite()
    {
        var ledgerPath = Path.Combine(Path.GetTempPath(), $"dry-run-ledger-{Guid.NewGuid():N}.json");
        try
        {
            var gateway = new MockTelegramGateway();
            var cv = new CvData
            {
                Name = "Candidate",
                YearsExperience = 4,
                Seniority = "Mid",
                Skills = new List<string> { "C#" },
            };
            var llm = new MockLlmClient("""{"category":"Strong","reasoning":"fit","confidence":0.95}""");
            var definition = HostGraph.Build(
                gateway,
                new PendingApprovalRegistry(),
                new ApplicationLedger.ApplicationLedger(ledgerPath),
                llm,
                cv,
                new RunStatsCollector(),
                dryRun: true);
            var posting = new JobPosting(
                "Backend Engineer", "Acme", "Mid", new List<string> { "C#" }, "desc",
                "https://example.com", "https://example.com/apply", ApplyChannels.ExternalPlatform);
            var state = new GraphState(new Dictionary<string, object>
            {
                [NormalizeJobPostingNode.JobPostingStateKey] = posting,
                [JobApplicationStateKeys.Company] = posting.Company,
                [JobApplicationStateKeys.Title] = posting.Title,
                [JobApplicationStateKeys.SourceUrl] = posting.SourceUrl,
            });

            await definition.CreateRun().RunAsync(HostGraph.DedupeCheckNodeName, state);

            Assert.True(state.Get<bool>(ScoreMatchNode.StageOnePassedStateKey));
            Assert.False(state.ContainsKey(RecordIfApprovedNode.OutcomeStateKey));
            Assert.Empty(gateway.SentMessages);
            Assert.False(File.Exists(ledgerPath));
        }
        finally
        {
            File.Delete(ledgerPath);
        }
    }
}
