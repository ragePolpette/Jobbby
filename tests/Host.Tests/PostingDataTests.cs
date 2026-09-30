using ApplicationLedger;
using Config;

namespace Host.Tests;

/// <summary>PR 6 data: presentation and pasted full text on records, in-place updates of ledger and run postings.</summary>
public class PostingDataTests
{
    private static ApplicationRecord Record(string company, string title, string outcome, DateTimeOffset at)
    {
        var key = PostingIdentity.Key(company, title);
        return new ApplicationRecord(key, company, title, null, at, outcome, PostingIdentity.Id(key), "motivo");
    }

    [Fact]
    public void Ledger_Update_AppendsANewVersion_KeepsIdentityAndHistory()
    {
        using var tmp = new TempDir();
        var ledger = new ApplicationLedger.ApplicationLedger(tmp.Path("applications.json"));
        var original = Record("Acme", "Cuoca", ApplicationOutcomes.Pending, DateTimeOffset.UtcNow.AddMinutes(-5));
        ledger.RecordOutcome(original);
        var message = new PresentationMessage("Gentile Acme…", DateTimeOffset.UtcNow, Edited: false, BasedOnFullText: false, new List<string> { "HACCP" });

        var updated = ledger.Update(original.PostingId!, record => record with { Presentation = message, PostingId = "altro", DedupeKey = "altro" });

        Assert.Equal(original.PostingId, updated!.PostingId);
        Assert.Equal(original.DedupeKey, updated.DedupeKey);
        Assert.Equal(ApplicationOutcomes.Pending, updated.Outcome);
        Assert.Equal(2, ledger.History(original.PostingId!).Count);
        var reloaded = new ApplicationLedger.ApplicationLedger(tmp.Path("applications.json"));
        Assert.Equal("HACCP", reloaded.Current(original.PostingId!)!.Presentation!.Matches.Single());
    }

    [Fact]
    public void Ledger_Update_UnknownId_IsNull()
    {
        using var tmp = new TempDir();
        var ledger = new ApplicationLedger.ApplicationLedger(tmp.Path("applications.json"));

        Assert.Null(ledger.Update("sconosciuto", record => record));
    }

    [Fact]
    public void Ledger_Recent_GivesCurrentRecords_NewestFirst()
    {
        using var tmp = new TempDir();
        var ledger = new ApplicationLedger.ApplicationLedger(tmp.Path("applications.json"));
        var old = Record("Acme", "Cuoca", ApplicationOutcomes.Pending, DateTimeOffset.UtcNow.AddHours(-2));
        ledger.RecordOutcome(old);
        ledger.RecordOutcome(Record("Beta", "Aiuto cuoco", ApplicationOutcomes.Shortlisted, DateTimeOffset.UtcNow.AddHours(-1)));
        ledger.Decide(old.PostingId!, ApplicationOutcomes.Approved, "ok");

        var recent = ledger.Recent(5);

        Assert.Equal(2, recent.Count);
        Assert.Equal("Acme", recent[0].Company);
        Assert.Equal(ApplicationOutcomes.Approved, recent[0].Outcome);
    }

    [Fact]
    public void Ledger_OldRecordsWithoutTheNewFields_StillLoad()
    {
        using var tmp = new TempDir();
        File.WriteAllText(tmp.Path("applications.json"), """[{"DedupeKey":"acme::cuoca","Company":"Acme","Title":"Cuoca","SourceUrl":null,"RecordedAt":"2026-09-01T00:00:00+00:00","Outcome":"Pending"}]""");

        var record = new ApplicationLedger.ApplicationLedger(tmp.Path("applications.json")).Pending().Single();

        Assert.Null(record.Presentation);
        Assert.Null(record.FullText);
    }

    [Fact]
    public void RunStore_UpdatePosting_ChangesOnlyThatPosting()
    {
        using var tmp = new TempDir();
        var store = new RunStore(new DataDir(tmp.Root));
        var a = new RunPosting("a", "Cuoca", "Acme", "https://x/a", "Adzuna", "Local", PostingStatus.Evaluated, "da decidere", Record("Acme", "Cuoca", ApplicationOutcomes.Pending, DateTimeOffset.UtcNow), null);
        var b = a with { PostingId = "b", Title = "Aiuto cuoco" };
        store.Save(new RunRecord { RunId = "20260930-120000-dry-abcdef12", Mode = "dry", Status = RunStatus.Completed, Postings = new() { a, b } });

        var updated = store.UpdatePosting("20260930-120000-dry-abcdef12", "b", posting => posting with { Summary = "cambiato" });

        Assert.Equal("cambiato", updated!.Summary);
        var run = store.Load("20260930-120000-dry-abcdef12")!;
        Assert.Equal("da decidere", run.Postings[0].Summary);
        Assert.Equal("cambiato", run.Postings[1].Summary);
        Assert.Null(store.UpdatePosting("20260930-120000-dry-abcdef12", "zzz", posting => posting));
        Assert.Null(store.UpdatePosting("../fuori", "a", posting => posting));
    }

    [Theory]
    [InlineData("tone", "amichevole")]
    [InlineData("length", "lunghissima")]
    [InlineData("language", "italiano")]
    [InlineData("opening", "Buongiorno {telefono}")]
    [InlineData("closing", "LONG")]
    public void Validator_RejectsInvalidPresentationSettings(string field, string value)
    {
        var p = JobbbySettings.Default.Presentation;
        if (value == "LONG") value = new string('x', 2001);
        p = field switch
        {
            "tone" => p with { Tone = value },
            "length" => p with { Length = value },
            "language" => p with { Language = value },
            "opening" => p with { Opening = value },
            _ => p with { Closing = value },
        };

        var error = Assert.Single(SettingsValidator.Validate(JobbbySettings.Default with { Presentation = p }, forRun: false));

        Assert.Equal($"presentation.{field}", error.Field);
    }

    [Theory]
    [InlineData("cordiale", "media", "en", "Buongiorno, sono l'assistente di {nome} per il ruolo di {ruolo} presso {azienda}.")]
    [InlineData("formale", "breve", "annuncio", "")]
    public void Validator_AcceptsValidPresentationSettings(string tone, string length, string language, string opening)
    {
        var p = JobbbySettings.Default.Presentation with { Tone = tone, Length = length, Language = language, Opening = opening };

        Assert.Empty(SettingsValidator.Validate(JobbbySettings.Default with { Presentation = p }, forRun: false));
    }
}
