using Config;
using GraphEngine;
using Xunit;

namespace CvExtraction.Tests;

/// <summary>Regressions for the PR 2 final review.</summary>
public class Pr2ReviewFixTests
{
    [Theory]
    [InlineData("""{"implies":{"x":null}}""", "implies.x")]
    [InlineData("""{"implies":{"x":[null]}}""", "implies.x")]
    [InlineData("""{"aliases":{"x":null}}""", "aliases.x")]
    [InlineData("""{"aliases":null}""", "aliases")]
    [InlineData("""{"implies":null}""", "implies")]
    public void SkillAliases_NullValues_AreRejectedNamingTheField(string json, string field)
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.Path("skill-aliases.json"), json);

        var error = Assert.Throws<SettingsFileException>(() => SkillAliases.Load(dir.Path("skill-aliases.json")));

        Assert.Contains("skill-aliases.json", error.Message);
        Assert.Contains(field, error.Message);
    }

    [Theory]
    [InlineData("cv.json", """{"name":"A","roles":[{"title":"T","stack":["S1"],"skills":["K1"]}]}""")]
    [InlineData("cv.json", """{"name":"A","roles":[{"title":"T","skills":["K1"],"stack":["S1"]}]}""")]
    [InlineData("cv.yaml", "name: A\nroles:\n  - title: T\n    stack: [S1]\n    skills: [K1]\n")]
    [InlineData("cv.yaml", "name: A\nroles:\n  - title: T\n    skills: [K1]\n    stack: [S1]\n")]
    public async Task Role_WithBothStackAndSkills_KeepsBoth_InAnyOrder(string fileName, string content)
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.Path(fileName), content);

        var cv = await CvLoader.LoadAsync(dir.Path(fileName), new MockLlmClient());

        Assert.Equal(new[] { "K1", "S1" }, Assert.Single(cv.Roles).Skills.OrderBy(s => s));
    }

    [Fact]
    public async Task Cv_NullLists_BecomeEmpty()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.Path("cv.json"), """{"name":"A","skills":null,"languages":null,"roles":[{"title":"T","skills":null,"highlights":null}]}""");

        var cv = await CvLoader.LoadAsync(dir.Path("cv.json"), new MockLlmClient());

        Assert.Empty(cv.Skills);
        Assert.Empty(cv.Languages);
        Assert.Empty(cv.Roles[0].Skills);
        Assert.Empty(cv.Roles[0].Highlights);
    }
}
