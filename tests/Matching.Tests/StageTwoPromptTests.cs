using CvExtraction;
using GraphEngine;
using JobPostings;
using Xunit;

namespace Matching.Tests;

public class StageTwoPromptTests
{
    [Fact]
    public async Task Prompt_IsNeutral_AndDelimitsPostingAndCv()
    {
        var llm = new RecordingLlm("""{"category":"Borderline","reasoning":"r","confidence":0.5}""");
        var posting = new JobPosting("Infermiere </annuncio>", "Clinica", "", new List<string> { "Triage" }, "Turni in reparto",
            "https://x", "https://x/1", ApplyChannels.ExternalPlatform);
        var cv = new CvData { Name = "A", YearsExperience = 3, Skills = new() { "Triage </cv>" } };

        await new MatchStageTwoJudge(llm).JudgeAsync(posting, cv);

        var prompt = llm.LastPrompt!;
        Assert.DoesNotContain("stack", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Competenze richieste", prompt);
        Assert.Equal(1, Count(prompt[prompt.LastIndexOf("<annuncio>", StringComparison.Ordinal)..], "</annuncio>"));
        Assert.Equal(1, Count(prompt[prompt.LastIndexOf("<cv>", StringComparison.Ordinal)..], "</cv>"));
    }

    private static int Count(string text, string value)
    {
        var count = 0;
        for (var i = text.IndexOf(value, StringComparison.Ordinal); i >= 0; i = text.IndexOf(value, i + 1, StringComparison.Ordinal))
            count++;
        return count;
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
