using ApplicationLedger;
using Xunit;

namespace Host.Tests;

public class CliCommandsTests
{
    private static string Seed(DataDir dataDir, string company, string title, string outcome, double confidence = 0.5)
    {
        var key = PostingIdentity.Key(company, title);
        new ApplicationLedger.ApplicationLedger(dataDir.ApplicationsPath).RecordOutcome(
            new ApplicationRecord(key, company, title, "https://x/" + title, DateTimeOffset.UtcNow, outcome, PostingIdentity.Id(key)) { Confidence = confidence });
        return PostingIdentity.Id(key);
    }

    [Fact]
    public void Pending_ListsIdTitleCompanyAndConfidence()
    {
        using var tmp = new TempDir();
        var dataDir = new DataDir(tmp.Root);
        var id = Seed(dataDir, "Clinic", "Nurse", ApplicationOutcomes.Pending, 0.42);
        Seed(dataDir, "Other", "Cook", ApplicationOutcomes.Shortlisted);
        var output = new StringWriter();

        var exitCode = CliCommands.Pending(dataDir, output);

        Assert.Equal(0, exitCode);
        var text = output.ToString();
        Assert.Contains(id, text);
        Assert.Contains("Nurse", text);
        Assert.Contains("Clinic", text);
        Assert.Contains("0.42", text);
        Assert.DoesNotContain("Cook", text);
    }

    [Theory]
    [InlineData("approve", ApplicationOutcomes.Approved)]
    [InlineData("reject", ApplicationOutcomes.Rejected)]
    [InlineData("applied", ApplicationOutcomes.Applied)]
    public void Decide_AppendsTheDecision_KeepingThePostingData(string action, string expected)
    {
        using var tmp = new TempDir();
        var dataDir = new DataDir(tmp.Root);
        var id = Seed(dataDir, "Clinic", "Nurse", ApplicationOutcomes.Pending, 0.42);

        var exitCode = CliCommands.Decide(dataDir, id, action, new StringWriter(), new StringWriter());

        Assert.Equal(0, exitCode);
        var ledger = new ApplicationLedger.ApplicationLedger(dataDir.ApplicationsPath);
        var current = ledger.Current(id)!;
        Assert.Equal(expected, current.Outcome);
        Assert.Equal(0.42, current.Confidence);
        Assert.Equal("https://x/Nurse", current.ApplyUrl ?? current.SourceUrl);
        Assert.Equal(2, ledger.History(id).Count);
        Assert.Empty(ledger.Pending());
    }

    [Theory]
    [InlineData("0123456789abcdef0123456789abcdef", "approve", "sconosciuto")]
    [InlineData(null, "maybe", "Azione")]
    public void Decide_UnknownIdOrAction_Exits2WithAMessage(string? id, string action, string expected)
    {
        using var tmp = new TempDir();
        var dataDir = new DataDir(tmp.Root);
        var known = Seed(dataDir, "Clinic", "Nurse", ApplicationOutcomes.Pending);
        var error = new StringWriter();

        var exitCode = CliCommands.Decide(dataDir, id ?? known, action, new StringWriter(), error);

        Assert.Equal(2, exitCode);
        Assert.Contains(expected, error.ToString());
        Assert.Equal(ApplicationOutcomes.Pending, new ApplicationLedger.ApplicationLedger(dataDir.ApplicationsPath).Current(known)!.Outcome);
    }
}
