using Discovery;
using GraphEngine;
using Xunit;

namespace Discovery.Tests;

public class DiscoveryEngineTests
{
    [Fact]
    public async Task DiscoverAsync_ComposesQueriesAndPropagatesLlmEvaluation()
    {
        var criteria = new DiscoveryCriteria
        {
            SearchIntent = "blog di ricette vegane facili",
            EvaluationCriteria = "popolarita basata su recensioni",
        };

        var intentResult = new SearchResult("Veggie Corner", "https://veggiecorner.example", "Ricette vegane semplici e veloci");
        var reputationQuery = $"{criteria.EvaluationCriteria} {intentResult.Title}";

        var webSearchClient = new MockWebSearchClient(query => query switch
        {
            _ when query == criteria.SearchIntent => new[] { intentResult },
            _ when query == reputationQuery =>
                new[] { new SearchResult("Recensioni Veggie Corner", "https://reviews.example/veggie-corner", "4.8 stelle su 500 recensioni") },
            _ => throw new InvalidOperationException($"Unexpected query: {query}"),
        });

        var llmClient = new MockLlmClient("""
            {"evaluationSummary":"Blog affidabile con ottime recensioni","reliabilityScore":9}
            """);

        var engine = new DiscoveryEngine(webSearchClient, llmClient);

        var candidates = await engine.DiscoverAsync(criteria);

        var candidate = Assert.Single(candidates);
        Assert.Equal("Veggie Corner", candidate.Name);
        Assert.Equal("https://veggiecorner.example", candidate.Url);
        Assert.Equal("Blog affidabile con ottime recensioni", candidate.EvaluationSummary);
        Assert.Equal(9, candidate.ReliabilityScore);

        Assert.Equal(new[] { criteria.SearchIntent, reputationQuery }, webSearchClient.Queries);
    }

    [Fact]
    public async Task DiscoverAsync_MultipleCandidates_EachGetsItsOwnReputationSearchAndEvaluation()
    {
        var criteria = new DiscoveryCriteria
        {
            SearchIntent = "negozi di dischi in vinile",
            EvaluationCriteria = "reputazione secondo i clienti",
        };

        var candidateA = new SearchResult("Vinyl House", "https://vinylhouse.example", "Ampia selezione di vinili");
        var candidateB = new SearchResult("Disco Corner", "https://discocorner.example", "Vinili rari e da collezione");

        var webSearchClient = new MockWebSearchClient(query => query switch
        {
            _ when query == criteria.SearchIntent => new[] { candidateA, candidateB },
            _ when query == $"{criteria.EvaluationCriteria} {candidateA.Title}" =>
                new[] { new SearchResult("Recensioni Vinyl House", "https://r.example/a", "Ottimo servizio") },
            _ when query == $"{criteria.EvaluationCriteria} {candidateB.Title}" =>
                new[] { new SearchResult("Recensioni Disco Corner", "https://r.example/b", "Servizio scadente") },
            _ => throw new InvalidOperationException($"Unexpected query: {query}"),
        });

        var llmClient = new MockLlmClient(new[]
        {
            """{"evaluationSummary":"Affidabile","reliabilityScore":8}""",
            """{"evaluationSummary":"Poco affidabile","reliabilityScore":3}""",
        });

        var engine = new DiscoveryEngine(webSearchClient, llmClient);

        var candidates = await engine.DiscoverAsync(criteria);

        Assert.Equal(2, candidates.Count);

        Assert.Equal("Vinyl House", candidates[0].Name);
        Assert.Equal("Affidabile", candidates[0].EvaluationSummary);
        Assert.Equal(8, candidates[0].ReliabilityScore);

        Assert.Equal("Disco Corner", candidates[1].Name);
        Assert.Equal("Poco affidabile", candidates[1].EvaluationSummary);
        Assert.Equal(3, candidates[1].ReliabilityScore);

        Assert.Equal(3, webSearchClient.Queries.Count); // 1 intent search + 2 reputation searches
    }

    [Fact]
    public async Task DiscoverAsync_NoCandidatesFound_ReturnsEmptyListAndSkipsReputationSearch()
    {
        var criteria = new DiscoveryCriteria { SearchIntent = "qualcosa di introvabile", EvaluationCriteria = "n/a" };
        var webSearchClient = new MockWebSearchClient(Array.Empty<SearchResult>());
        var llmClient = new MockLlmClient("should not be called");

        var engine = new DiscoveryEngine(webSearchClient, llmClient);

        var candidates = await engine.DiscoverAsync(criteria);

        Assert.Empty(candidates);
        Assert.Equal(new[] { criteria.SearchIntent }, webSearchClient.Queries);
    }
}
