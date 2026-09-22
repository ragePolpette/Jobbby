using Config;
using GraphEngine;
using Notifications;
using Xunit;

namespace Discovery.Tests;

public class DiscoveryReviewServiceTests
{
    [Fact]
    public async Task DiscoverAndReviewAsync_OnlyPersistsExplicitlyApprovedReliableSources()
    {
        var search = new MockWebSearchClient(query => query switch
        {
            "fonti" => new[]
            {
                new SearchResult("Reliable", "https://reliable.example", "good"),
                new SearchResult("Weak", "https://weak.example", "unknown"),
            },
            _ => Array.Empty<SearchResult>(),
        });
        var llm = new MockLlmClient(new[]
        {
            """{"evaluationSummary":"trusted","reliabilityScore":9}""",
            """{"evaluationSummary":"unclear","reliabilityScore":4}""",
        });
        var gateway = new MockTelegramGateway();
        var registry = new PendingApprovalRegistry();
        gateway.ReplyReceived += registry.OnReply;
        var service = new DiscoveryReviewService(new DiscoveryEngine(search, llm), gateway, registry, TimeSpan.FromSeconds(2));

        var reviewTask = service.DiscoverAndReviewAsync(new DiscoveryCriteria { SearchIntent = "fonti", EvaluationCriteria = "reputation" });
        await WaitForMessageAsync(gateway);
        gateway.SimulateReply(1, "sì");
        var result = await reviewTask;

        var approved = Assert.Single(result.ApprovedSources);
        Assert.Equal("Reliable", approved.Name);
        Assert.Single(gateway.SentMessages);
    }

    [Fact]
    public void SaveApproved_DeduplicatesByNormalizedUrl()
    {
        var path = Path.Combine(Path.GetTempPath(), $"approved-{Guid.NewGuid():N}.json");
        try
        {
            ApprovedSourceStore.SaveApproved(path, new[]
            {
                new SourceDefinition { Name = "First", BaseUrl = "https://source.example/", Type = "discovered" },
            });
            ApprovedSourceStore.SaveApproved(path, new[]
            {
                new SourceDefinition { Name = "Updated", BaseUrl = "https://source.example", Type = "discovered" },
            });

            var source = Assert.Single(ApprovedSourceStore.Load(path));
            Assert.Equal("Updated", source.Name);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static async Task WaitForMessageAsync(MockTelegramGateway gateway)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (gateway.SentMessages.Count == 0 && DateTime.UtcNow < deadline)
            await Task.Delay(10);
    }
}
