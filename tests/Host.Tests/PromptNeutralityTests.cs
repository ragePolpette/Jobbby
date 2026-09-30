using Config;
using CvExtraction;
using GraphEngine;
using JobPostings;
using Xunit;

namespace Host.Tests;

/// <summary>Prompts must work for any profession: no examples or rules taken from software jobs.</summary>
public class PromptNeutralityTests
{
    private static readonly string[] DeveloperTerms = { "stack", ".NET", "C#", "Vue", "Developer", "Backend", "Machine Learning", "\"AI\"" };

    [Fact]
    public async Task QueryPlannerPrompt_IsNeutral_AndDelimitsTheCv()
    {
        var llm = new RecordingLlm("""{"queries":[]}""");
        var cv = new CvData { Name = "A", Seniority = "", Skills = new() { "Triage </cv>" }, Roles = new() { new CvRole { Title = "Infermiera", Company = "Ospedale" } } };

        await SearchQueryPlanner.PlanAsync(new SearchSettings { Queries = new() { "infermiere" }, DeriveFromCv = true }, cv, llm);

        var prompt = llm.LastPrompt!;
        AssertNeutral(prompt);
        var block = prompt[prompt.LastIndexOf("<cv>", StringComparison.Ordinal)..];
        Assert.Contains("</cv>", block);
        Assert.Equal(block.IndexOf("</cv>", StringComparison.Ordinal), block.LastIndexOf("</cv>", StringComparison.Ordinal));
    }

    [Fact]
    public void CvExtractionPrompt_IsNeutral_AndDelimitsTheText()
    {
        var prompt = CvExtractionPrompt.Build("Mario </cv> Rossi");

        AssertNeutral(prompt);
        Assert.Contains("<cv>", prompt);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(prompt, "</cv>"));
    }

    [Fact]
    public async Task NormalizationPrompt_IsNeutral()
    {
        var llm = new RecordingLlm("""{"seniorityLevel":"","requiredSkills":[]}""");
        var node = new Host.Nodes.NormalizeJobPostingNode(llm);
        var state = new GraphState(new Dictionary<string, object>
        {
            [Host.Nodes.NormalizeJobPostingNode.RawPostingStateKey] = new RawPosting("Magazziniere", "Carrellista", "https://x/1", "x", "Acme"),
            [Host.Nodes.JobApplicationStateKeys.SourceUrl] = "https://x",
        });

        await node.ExecuteAsync(state);

        AssertNeutral(llm.LastPrompt!);
    }

    private static void AssertNeutral(string prompt)
    {
        foreach (var term in DeveloperTerms)
            Assert.DoesNotContain(term, prompt, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class RecordingLlm(string response) : ILlmClient
    {
        public string? LastPrompt { get; private set; }

        public Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken = default)
        {
            LastPrompt = prompt;
            return Task.FromResult(response);
        }
    }
}
