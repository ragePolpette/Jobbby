using Config;
using Xunit;

namespace CvExtraction.Tests;

public class SkillAliasesTests
{
    [Fact]
    public void Load_MissingFile_IsEmpty()
    {
        using var dir = new TempDir();

        var aliases = SkillAliases.Load(dir.Path("skill-aliases.json"));

        Assert.Empty(aliases.Aliases);
        Assert.Empty(aliases.Implies);
    }

    [Fact]
    public void Load_ReadsAliasesAndImplications()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.Path("skill-aliases.json"), """{"aliases":{"RCP":"rianimazione cardiopolmonare"},"implies":{"bls-d":["bls"]}}""");

        var aliases = SkillAliases.Load(dir.Path("skill-aliases.json"));

        Assert.Equal("rianimazione cardiopolmonare", aliases.Aliases["RCP"]);
        Assert.Equal(new[] { "bls" }, aliases.Implies["bls-d"]);
    }

    [Fact]
    public void Load_Malformed_ThrowsNamingTheFile()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.Path("skill-aliases.json"), """{"aliases": [1, 2]}""");

        var error = Assert.Throws<SettingsFileException>(() => SkillAliases.Load(dir.Path("skill-aliases.json")));

        Assert.Contains("skill-aliases.json", error.Message);
    }
}
