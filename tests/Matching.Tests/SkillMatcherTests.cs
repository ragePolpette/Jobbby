using Xunit;

namespace Matching.Tests;

public class SkillMatcherTests
{
    [Theory]
    [InlineData(".NET Core", ".NET")]
    [InlineData(".NET", ".NET Core")]
    [InlineData(".NET 8", ".NET")]
    [InlineData("ASP.NET Core", ".NET")]
    [InlineData("API REST", "REST API")]
    [InlineData("RESTful APIs", "REST API")]
    [InlineData("Entity Framework", "Entity Framework Core")]
    [InlineData("EF Core", "Entity Framework")]
    [InlineData("Vue.js", "Vue 3")]
    [InlineData("SQL Server", "SQL")]
    [InlineData("SQL Server", "MSSQL")]
    [InlineData("csharp", "C#")]
    [InlineData("TypeScript", "JavaScript")]
    public void Covers_EquivalentOrMoreSpecificSkill(string candidateSkill, string requiredSkill)
    {
        Assert.True(new SkillMatcher(new[] { candidateSkill }).Covers(requiredSkill));
    }

    [Theory]
    [InlineData("Java", "JavaScript")]
    [InlineData("JavaScript", "Java")]
    [InlineData("C#", "C++")]
    [InlineData("C", "C#")]
    [InlineData(".NET", "ASP.NET Core")]
    [InlineData("React", "Rust")]
    public void DoesNotCover_UnrelatedOrBroaderSkill(string candidateSkill, string requiredSkill)
    {
        Assert.False(new SkillMatcher(new[] { candidateSkill }).Covers(requiredSkill));
    }

    [Fact]
    public void Covers_BlankRequiredSkill_IsFalse()
    {
        Assert.False(new SkillMatcher(new[] { "C#" }).Covers("  "));
    }
}
