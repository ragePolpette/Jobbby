using Config;
using CvExtraction;
using GraphEngine;
using Xunit;

namespace Host.Tests;

public class SearchQueryPlannerTests
{
    private static readonly CvData Cv = new()
    {
        Name = "Candidate",
        YearsExperience = 8,
        Seniority = "Senior",
        Skills = new() { "C#", ".NET", "LLM" },
    };

    [Fact]
    public async Task PlanAsync_DerivationDisabled_UsesConfiguredQueriesWithoutCallingTheLlm()
    {
        var llm = new RecordingLlm("""{"queries":["unused"]}""");

        var plan = await SearchQueryPlanner.PlanAsync(
            new SearchSettings { Queries = new() { " .NET developer ", "AI engineer", ".net DEVELOPER" } }, Cv, llm);

        Assert.Equal(new[] { ".NET developer", "AI engineer" }, plan.Queries);
        Assert.Equal(0, llm.Calls);
    }

    [Fact]
    public async Task PlanAsync_DerivationEnabled_AppendsDistinctDerivedQueriesUpToTheLimit()
    {
        var llm = new RecordingLlm("""{"queries":["AI engineer","Backend Developer C#","LLM Engineer","Tech Lead .NET"]}""");

        var plan = await SearchQueryPlanner.PlanAsync(
            new SearchSettings { Queries = new() { ".NET developer", "AI engineer" }, DeriveFromCv = true, MaxDerivedQueries = 3 }, Cv, llm);

        Assert.Equal(new[] { ".NET developer", "AI engineer", "Backend Developer C#", "LLM Engineer" }, plan.Queries);
        Assert.Equal(new[] { "AI engineer", "Backend Developer C#", "LLM Engineer" }, plan.DerivedQueries);
        Assert.Contains("C#, .NET, LLM", llm.LastPrompt);
        Assert.Contains(".NET developer, AI engineer", llm.LastPrompt);
    }

    [Fact]
    public async Task PlanAsync_DerivationFails_FallsBackToConfiguredQueries()
    {
        var plan = await SearchQueryPlanner.PlanAsync(
            new SearchSettings { Queries = new() { ".NET developer" }, DeriveFromCv = true }, Cv, new RecordingLlm("not json"));

        Assert.Equal(new[] { ".NET developer" }, plan.Queries);
        Assert.NotNull(plan.DerivationError);
    }

    [Fact]
    public async Task PlanAsync_NoQueriesAtAll_Throws()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SearchQueryPlanner.PlanAsync(new SearchSettings { DeriveFromCv = true }, Cv, new RecordingLlm("not json")));

        Assert.Contains("deriving them from the CV failed", error.Message);
    }

    private sealed class RecordingLlm(string response) : ILlmClient
    {
        public int Calls { get; private set; }
        public string? LastPrompt { get; private set; }

        public Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastPrompt = prompt;
            return Task.FromResult(response);
        }
    }
}
