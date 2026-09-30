using ApplicationLedger;
using Xunit;

namespace CvExtraction.Tests;

public class LedgerStateTests
{
    private static ApplicationRecord Record(string company, string title, string outcome, DateTimeOffset at)
    {
        var key = PostingIdentity.Key(company, title);
        return new ApplicationRecord(key, company, title, "https://x/1", at, outcome, PostingIdentity.Id(key));
    }

    [Fact]
    public void Current_IsTheMostRecentRecord_AndPendingListsOnlyUndecided()
    {
        using var dir = new TempDir();
        var ledger = new ApplicationLedger.ApplicationLedger(dir.Path("applications.json"));
        var t0 = DateTimeOffset.UtcNow.AddHours(-2);
        ledger.RecordOutcome(Record("Acme", "Contabile", ApplicationOutcomes.Pending, t0));
        ledger.RecordOutcome(Record("Acme", "Contabile", ApplicationOutcomes.Approved, t0.AddHours(1)));
        ledger.RecordOutcome(Record("Beta", "Contabile", ApplicationOutcomes.Pending, t0));

        var acme = PostingIdentity.Id(PostingIdentity.Key("Acme", "Contabile"));

        Assert.Equal(ApplicationOutcomes.Approved, ledger.Current(acme)!.Outcome);
        Assert.Equal(new[] { "Beta" }, ledger.Pending().Select(r => r.Company));
        Assert.Equal(2, ledger.History(acme).Count);
    }

    [Fact]
    public void FullRecord_RoundTrips()
    {
        using var dir = new TempDir();
        var path = dir.Path("applications.json");
        var record = Record("Acme", "Contabile", ApplicationOutcomes.Pending, DateTimeOffset.UtcNow) with
        {
            RunId = "run-1",
            SourceName = "Adzuna",
            ApplyUrl = "https://x/1",
            Excerpt = "Estratto",
            RequiredSkills = new() { "Contabilità" },
            WorkMode = "Onsite",
            Location = "Bologna",
            SalaryMaximum = 40000m,
            MinYearsExperience = 3,
            Seniority = "Mid",
            Confidence = 0.55,
            Category = "Borderline",
            Reasoning = "r",
            MissingRequirements = new() { "m" },
            Warnings = new() { "w" },
        };
        new ApplicationLedger.ApplicationLedger(path).RecordOutcome(record);

        var loaded = new ApplicationLedger.ApplicationLedger(path).Current(record.PostingId!)!;

        Assert.Equal("run-1", loaded.RunId);
        Assert.Equal(new[] { "Contabilità" }, loaded.RequiredSkills!);
        Assert.Equal(0.55, loaded.Confidence);
        Assert.Equal("Bologna", loaded.Location);
        Assert.Equal(new[] { "w" }, loaded.Warnings!);
    }

    [Fact]
    public void LegacyLedger_IsRekeyedWithTheUnifiedIdentity()
    {
        using var dir = new TempDir();
        var path = dir.Path("applications.json");
        // Written by an older version: old key normalisation, no PostingId.
        File.WriteAllText(path, """[{"DedupeKey":"acme s.r.l.::backend engineer","Company":"Acme S.r.l.","Title":"Backend Engineer","SourceUrl":null,"RecordedAt":"2026-09-01T00:00:00+00:00","Outcome":"Rejected"}]""");

        var ledger = new ApplicationLedger.ApplicationLedger(path);

        Assert.True(ledger.HasBeenProcessed(PostingIdentity.Key("ACME SRL", "backend engineer")));
        Assert.NotNull(ledger.Current(PostingIdentity.Id(PostingIdentity.Key("Acme", "Backend Engineer"))));
    }

    [Fact]
    public void ExtraSuffixes_ApplyToTheLedgerToo()
    {
        using var dir = new TempDir();
        var path = dir.Path("applications.json");
        File.WriteAllText(path, """[{"DedupeKey":"x","Company":"Acme Holding","Title":"Contabile","SourceUrl":null,"RecordedAt":"2026-09-01T00:00:00+00:00","Outcome":"Rejected"}]""");

        var ledger = new ApplicationLedger.ApplicationLedger(path, new[] { "holding" });

        Assert.True(ledger.HasBeenProcessed(PostingIdentity.Key("Acme", "Contabile", new[] { "holding" })));
    }
}
