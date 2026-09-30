using Config;
using Xunit;

namespace Matching.Tests;

public class SkillMatcherTests
{
    /// <summary>A sample alias file, as a software developer might keep in their DataDir: data, not code.</summary>
    private static readonly SkillAliases DeveloperAliases = new()
    {
        Aliases = new()
        {
            ["csharp"] = "c#",
            ["api rest"] = "rest api",
            ["restful apis"] = "rest api",
            ["ef core"] = "entity framework core",
            ["vue"] = "vue.js",
            ["mssql"] = "sql server",
        },
        Implies = new()
        {
            ["asp.net core"] = new() { ".net", ".net core" },
            ["typescript"] = new() { "javascript" },
            ["entity framework core"] = new() { "entity framework" },
        },
    };

    [Theory]
    [InlineData(".NET Core", ".NET")]
    [InlineData(".NET 8", ".NET")]
    [InlineData("SQL Server", "SQL")]
    [InlineData("Triage infermieristico", "Triage")]
    [InlineData("  Contabilità   generale ", "contabilità generale")]
    [InlineData("Excel 2019", "excel")]
    public void Covers_GenericRules_WithoutAnyAlias(string candidateSkill, string requiredSkill)
    {
        Assert.True(new SkillMatcher(new[] { candidateSkill }).Covers(requiredSkill));
    }

    [Theory]
    [InlineData("Gestione", "Gestione magazzino")]
    [InlineData("Excel", "Excel VBA")]
    [InlineData("Entity Framework", "Entity Framework Core")]
    [InlineData(".NET", ".NET Core")]
    public void DoesNotCover_SpecificRequirement_WithAGenericSkill(string candidateSkill, string requiredSkill)
    {
        // Only "X Y" covers "X": a generic skill must not satisfy a specific (possibly must-have) requirement.
        Assert.False(new SkillMatcher(new[] { candidateSkill }).Covers(requiredSkill));
    }

    [Theory]
    [InlineData("csharp", "C#")]
    [InlineData("API REST", "REST API")]
    [InlineData("TypeScript", "JavaScript")]
    [InlineData("ASP.NET Core", ".NET")]
    public void DoesNotCover_ProfessionSpecificEquivalences_WithoutAliases(string candidateSkill, string requiredSkill)
    {
        Assert.False(new SkillMatcher(new[] { candidateSkill }).Covers(requiredSkill));
    }

    [Theory]
    [InlineData("csharp", "C#")]
    [InlineData("API REST", "REST API")]
    [InlineData("RESTful APIs", "REST API")]
    [InlineData("EF Core", "Entity Framework")]
    [InlineData("Vue.js", "Vue 3")]
    [InlineData("SQL Server", "MSSQL")]
    [InlineData("TypeScript", "JavaScript")]
    [InlineData("ASP.NET Core", ".NET")]
    public void Covers_WithAliases(string candidateSkill, string requiredSkill)
    {
        Assert.True(new SkillMatcher(new[] { candidateSkill }, DeveloperAliases).Covers(requiredSkill));
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
        Assert.False(new SkillMatcher(new[] { candidateSkill }, DeveloperAliases).Covers(requiredSkill));
    }

    [Fact]
    public void Covers_BlankRequiredSkill_IsFalse()
    {
        Assert.False(new SkillMatcher(new[] { "C#" }).Covers("  "));
    }
}
