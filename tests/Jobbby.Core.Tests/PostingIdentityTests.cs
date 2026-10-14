using ApplicationLedger;
using Xunit;

namespace Jobbby.Core.Tests;

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

    [Fact]
    public void Key_WithoutCompany_PrefersTheSourceIdOverTheLink()
    {
        var byId = PostingIdentity.Key("", "Nurse", applyUrl: "https://x/1?utm=a", sourceId: "42");

        Assert.Equal(byId, PostingIdentity.Key("", "Nurse", applyUrl: "https://x/other", sourceId: "42"));
        Assert.NotEqual(byId, PostingIdentity.Key("", "Nurse", applyUrl: "https://x/1?utm=a", sourceId: "43"));
        Assert.Equal(PostingIdentity.Key("Clinic", "Nurse"), PostingIdentity.Key("Clinic", "Nurse", applyUrl: "https://x/1", sourceId: "42"));
    }

    [Theory]
    [InlineData("https://WWW.Adzuna.it/land/ad/1/", "https://www.adzuna.it/land/ad/1")]
    [InlineData("https://www.adzuna.it/land/ad/1#apply", "https://www.adzuna.it/land/ad/1")]
    [InlineData(" https://www.adzuna.it/land/ad/1?se=x ", "https://www.adzuna.it/land/ad/1?se=x")]
    [InlineData("https://host:8443/a/", "https://host:8443/a")]
    [InlineData("mailto:hr@example.com", "mailto:hr@example.com")]
    public void NormalizeUrl_IgnoresCaseOfHostFragmentAndTrailingSlash(string url, string expected)
    {
        Assert.Equal(expected, PostingIdentity.NormalizeUrl(url));
    }

    [Fact]
    public void Ledger_RekeysFromTheSourceCompany_NotTheLlmOne()
    {
        using var dir = new TempDir();
        var key = PostingIdentity.Key("", "Nurse", applyUrl: "https://x/9", sourceId: "42");
        var ledger = new ApplicationLedger.ApplicationLedger(dir.Path("applications.json"));
        ledger.RecordOutcome(new ApplicationRecord(key, "Clinic From Llm", "Nurse", "https://x/9", DateTimeOffset.UtcNow, ApplicationOutcomes.Rejected)
        {
            ApplyUrl = "https://x/9",
            SourceCompany = "",
            SourcePostingId = "42",
        });

        var reloaded = new ApplicationLedger.ApplicationLedger(dir.Path("applications.json"));

        Assert.True(reloaded.HasBeenProcessed(key));
        Assert.False(reloaded.HasBeenProcessed(PostingIdentity.Key("Clinic From Llm", "Nurse")));
    }
}
