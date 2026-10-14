using CvExtraction;
using GraphEngine;
using Host.Nodes;
using JobPostings;
using Microsoft.Extensions.Configuration;
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
            })
            .Build();

        var options = DryRunOptions.FromConfiguration(configuration, Path.GetTempPath());

        Assert.True(options.Enabled);
        Assert.Equal(20, options.MaxPostingsPerSource);
    }

    [Fact]
    public async Task Graph_DryRunComputesTheOutcomeWithoutLedgerWrite()
    {
        var ledgerPath = Path.Combine(Path.GetTempPath(), $"dry-run-ledger-{Guid.NewGuid():N}.json");
        try
        {
            var cv = new CvData
            {
                Name = "Candidate",
                YearsExperience = 4,
                Seniority = "Mid",
                Skills = new List<string> { "C#" },
            };
            var llm = new MockLlmClient("""{"category":"Strong","reasoning":"fit","confidence":0.95}""");
            var definition = HostGraph.Build(
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
            Assert.Equal(ApplicationLedger.ApplicationOutcomes.Shortlisted, state.Get<string>(RecordOutcomeNode.OutcomeStateKey));
            Assert.False(File.Exists(ledgerPath));
        }
        finally
        {
            File.Delete(ledgerPath);
        }
    }
}
