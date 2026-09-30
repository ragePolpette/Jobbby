using GraphEngine;
using Xunit;

namespace CvExtraction.Tests;

public class CvSkillsCompatibilityTests
{
    [Theory]
    [InlineData("cv.json", """{"name":"A","roles":[{"title":"T","company":"C","stack":["Triage","BLS"]}]}""")]
    [InlineData("cv.json", """{"name":"A","roles":[{"title":"T","company":"C","skills":["Triage","BLS"]}]}""")]
    [InlineData("cv.yaml", "name: A\nroles:\n  - title: T\n    company: C\n    stack: [Triage, BLS]\n")]
    [InlineData("cv.yaml", "name: A\nroles:\n  - title: T\n    company: C\n    skills: [Triage, BLS]\n")]
    public async Task Role_ReadsSkills_AndLegacyStack(string fileName, string content)
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.Path(fileName), content);

        var cv = await CvLoader.LoadAsync(dir.Path(fileName), new MockLlmClient());

        Assert.Equal(new[] { "Triage", "BLS" }, Assert.Single(cv.Roles).Skills);
    }

    [Fact]
    public async Task Cv_ReadsLocation()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.Path("cv.json"), """{"name":"A","location":"Bologna"}""");

        var cv = await CvLoader.LoadAsync(dir.Path("cv.json"), new MockLlmClient());

        Assert.Equal("Bologna", cv.Location);
    }

    [Fact]
    public void ExtractionPrompt_AsksForTheCandidateLocation()
    {
        Assert.Contains("\"location\"", CvExtractionPrompt.Build("text"));
    }

    [Fact]
    public void Role_SerializesAsSkills()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new CvRole { Skills = new() { "Triage" } });

        Assert.Contains("\"skills\"", json);
        Assert.DoesNotContain("stack", json);
    }
}
