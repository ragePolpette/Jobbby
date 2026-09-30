using ApplicationLedger;
using Xunit;

namespace CvExtraction.Tests;

public class PostingIdentityTests
{
    [Theory]
    [InlineData("Acme S.r.l.", "ACME SRL")]
    [InlineData("Foo GmbH & Co. KG", "Foo GmbH")]
    [InlineData("Foo GmbH", "Foo")]
    [InlineData("Nordic Oy Ab", "Nordic")]
    [InlineData("Acme S.r.l. Unipersonale", "Acme")]
    [InlineData("  Klinik   Nord  ", "klinik nord")]
    public void Key_SameCompanySpelledDifferently_IsTheSame(string a, string b)
    {
        Assert.Equal(PostingIdentity.Key(a, "Pflegefachkraft"), PostingIdentity.Key(b, "pflegefachkraft"));
    }

    [Theory]
    [InlineData("Acme Italia", "Acme")]
    [InlineData("Studio Rossi", "Studio Bianchi")]
    [InlineData("Atlas", "At")]
    public void Key_DifferentCompanies_StayDifferent(string a, string b)
    {
        Assert.NotEqual(PostingIdentity.Key(a, "Contabile"), PostingIdentity.Key(b, "Contabile"));
    }

    [Fact]
    public void Key_CompanyMadeOnlyOfASuffix_IsNotEmptied()
    {
        Assert.NotEqual(PostingIdentity.Key("", "Contabile"), PostingIdentity.Key("AB", "Contabile"));
    }

    [Fact]
    public void Key_ExtraSuffixes_AreConfigurable()
    {
        Assert.NotEqual(PostingIdentity.Key("Acme Holding", "t"), PostingIdentity.Key("Acme", "t"));
        Assert.Equal(PostingIdentity.Key("Acme Holding", "t", new[] { "Holding" }), PostingIdentity.Key("Acme", "t", new[] { "holding" }));
    }

    [Fact]
    public void Id_IsStable32HexCharacters()
    {
        var key = PostingIdentity.Key("Acme", "Contabile");

        var id = PostingIdentity.Id(key);

        Assert.Matches("^[0-9a-f]{32}$", id);
        Assert.Equal(id, PostingIdentity.Id(PostingIdentity.Key("ACME", "contabile")));
        Assert.NotEqual(id, PostingIdentity.Id(PostingIdentity.Key("Acme", "Contabile senior")));
    }

    [Fact]
    public void DedupeKey_DelegatesToPostingIdentity()
    {
        Assert.Equal(PostingIdentity.Key("Acme S.r.l.", "Contabile"), DedupeKey.Normalize("Acme S.r.l.", "Contabile"));
    }
}
