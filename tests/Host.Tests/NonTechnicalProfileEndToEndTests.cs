using System.Collections.Concurrent;
using Config;
using CvExtraction;
using GraphEngine;
using JobPostings;
using Xunit;

namespace Host.Tests;

/// <summary>
/// A nurse in Germany, end to end with a fake source and LLM: nothing in the pipeline may
/// assume a software profile or an Italian search.
/// </summary>
public class NonTechnicalProfileEndToEndTests
{
    private static readonly string[] DeveloperTerms = { "stack", ".NET", "C#", "Vue", "Developer", "Backend", "sviluppatore", "Machine Learning" };

    [Fact]
    public async Task NurseInGermany_RunsWithoutAnyDeveloperConcept()
    {
        using var tmp = new TempDir();
        var cv = await CvLoader.LoadAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "cv-nurse-de.json"), new MockLlmClient());
        var source = new MockJobSource(request => new[]
        {
            new RawPosting($"Pflegefachkraft Intensivstation ({request.Query})", "Wir suchen eine Pflegefachkraft mit Erfahrung in der Intensivpflege.",
                "https://jobs.example/1", "api.adzuna.com", "Klinik Nord GmbH", DateTimeOffset.UtcNow.AddHours(-2), "Köln, Nordrhein-Westfalen"),
        });
        var llm = new ProfileLlm();
        var runner = new JobbbyRunner(new DataDir(tmp.Root),
            new RunDependencies(source, llm, new[] { new SourceDefinition { Name = "Adzuna", BaseUrl = "https://api.adzuna.com" } }));
        var d = JobbbySettings.Default;
        var settings = d with
        {
            Area = d.Area with { Country = "de", DistanceKm = 25 },
            Searches = d.Searches with { Queries = new(), DeriveFromCv = true, MaxDerivedQueries = 1 },
            Salary = d.Salary with { MinimumYearly = 42000 },
        };
        var events = new ConcurrentBag<RunEvent>();

        var summary = await runner.RunAsync(settings, cv, RunMode.Dry, new CollectingProgress(events), CancellationToken.None);

        Assert.False(summary.Cancelled);
        var request = Assert.Single(source.RequestsReceived);
        Assert.Equal(new JobSearchRequest("Pflegefachkraft", "Köln", 25, SearchSweep.Local), request);
        Assert.Equal(1, summary.Report.TotalFetched);
        Assert.Equal(0, summary.Report.RejectedStageOne);
        Assert.Equal(1, summary.Report.StageTwoBreakdown["Strong"]);
        Assert.Contains(events, e => e.Kind == "evaluated" && e.Message.Contains("Pflegefachkraft"));
        Assert.Equal(3, llm.Prompts.Count);
        foreach (var prompt in llm.Prompts)
            foreach (var term in DeveloperTerms)
                Assert.DoesNotContain(term, prompt, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Answers the planner, normalization and stage-two prompts like a model would for this profile.</summary>
    private sealed class ProfileLlm : ILlmClient
    {
        public ConcurrentQueue<string> Prompts { get; } = new();

        public Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken = default)
        {
            Prompts.Enqueue(prompt);
            if (prompt.Contains("query di ricerca", StringComparison.Ordinal))
                return Task.FromResult("""{"queries":["Pflegefachkraft"]}""");
            if (prompt.Contains("Estrai le seguenti informazioni", StringComparison.Ordinal))
                return Task.FromResult("""{"seniorityLevel":"Fachkraft","requiredSkills":["Intensivpflege"],"workMode":"onsite","minYearsExperience":2}""");
            return Task.FromResult("""{"category":"Strong","reasoning":"Erfahrung in der Intensivpflege passt.","confidence":0.8}""");
        }
    }

    private sealed class CollectingProgress(ConcurrentBag<RunEvent> events) : IProgress<RunEvent>
    {
        public void Report(RunEvent value) => events.Add(value);
    }
}
