using ApplicationLedger;
using Xunit;

namespace CvExtraction.Tests;

public class ApplicationLedgerTests
{
    [Fact]
    public void RecordApplied_ThenHasApplied_FindsTheSameKey()
    {
        var path = Path.Combine(Path.GetTempPath(), $"applications-{Guid.NewGuid():N}.json");
        try
        {
            var ledger = new ApplicationLedger.ApplicationLedger(path);
            var key = DedupeKey.Normalize("Acme Corp", "Backend Engineer");

            Assert.False(ledger.HasApplied(key));

            ledger.RecordApplied(new ApplicationRecord(key, "Acme Corp", "Backend Engineer", "https://acme.example/job/1", DateTimeOffset.UtcNow));

            Assert.True(ledger.HasApplied(key));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("Acme Corp", "acme corp")]
    [InlineData("Acme Corp", "  acme   corp  ")]
    [InlineData("ACME CORP", "Acme Corp")]
    public void Normalize_TreatsCasingAndWhitespaceVariantsAsTheSameKey(string a, string b)
    {
        Assert.Equal(DedupeKey.Normalize(a, "Backend Engineer"), DedupeKey.Normalize(b, "Backend Engineer"));
    }

    [Fact]
    public void Normalize_DifferentTitles_ProduceDifferentKeys()
    {
        Assert.NotEqual(
            DedupeKey.Normalize("Acme Corp", "Backend Engineer"),
            DedupeKey.Normalize("Acme Corp", "Frontend Engineer"));
    }

    [Fact]
    public void ReloadedLedger_FromSameFile_SeesRecordsWrittenByPreviousInstance()
    {
        var path = Path.Combine(Path.GetTempPath(), $"applications-{Guid.NewGuid():N}.json");
        try
        {
            var key = DedupeKey.Normalize("Acme Corp", "Backend Engineer");
            var first = new ApplicationLedger.ApplicationLedger(path);
            first.RecordApplied(new ApplicationRecord(key, "Acme Corp", "Backend Engineer", null, DateTimeOffset.UtcNow));

            var second = new ApplicationLedger.ApplicationLedger(path);

            Assert.True(second.HasApplied(key));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void NewLedger_WithNoExistingFile_StartsEmpty()
    {
        var path = Path.Combine(Path.GetTempPath(), $"applications-{Guid.NewGuid():N}.json");
        File.Delete(path); // guarantee it does not exist

        var ledger = new ApplicationLedger.ApplicationLedger(path);

        Assert.False(ledger.HasApplied(DedupeKey.Normalize("Acme Corp", "Backend Engineer")));
    }
}
