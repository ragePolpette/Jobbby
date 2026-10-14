using ApplicationLedger;
using Xunit;

namespace Jobbby.Core.Tests;

public class ApplicationLedgerTests
{
    [Fact]
    public void RecordApplied_ThenHasBeenProcessed_FindsTheSameKey()
    {
        var path = Path.Combine(Path.GetTempPath(), $"applications-{Guid.NewGuid():N}.json");
        try
        {
            var ledger = new ApplicationLedger.ApplicationLedger(path);
            var key = DedupeKey.Normalize("Acme Corp", "Backend Engineer");

            Assert.False(ledger.HasBeenProcessed(key));

            ledger.RecordOutcome(new ApplicationRecord(
                key, "Acme Corp", "Backend Engineer", "https://acme.example/job/1", DateTimeOffset.UtcNow, ApplicationOutcomes.Applied));

            Assert.True(ledger.HasBeenProcessed(key));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RecordApplied_WithRejectedOutcome_StillCountsAsProcessed()
    {
        var path = Path.Combine(Path.GetTempPath(), $"applications-{Guid.NewGuid():N}.json");
        try
        {
            var ledger = new ApplicationLedger.ApplicationLedger(path);
            var key = DedupeKey.Normalize("Acme Corp", "Backend Engineer");

            ledger.RecordOutcome(new ApplicationRecord(
                key, "Acme Corp", "Backend Engineer", null, DateTimeOffset.UtcNow, ApplicationOutcomes.Rejected));

            Assert.True(ledger.HasBeenProcessed(key));
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
            first.RecordOutcome(new ApplicationRecord(
                key, "Acme Corp", "Backend Engineer", null, DateTimeOffset.UtcNow, ApplicationOutcomes.Applied));

            var second = new ApplicationLedger.ApplicationLedger(path);

            Assert.True(second.HasBeenProcessed(key));
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

        Assert.False(ledger.HasBeenProcessed(DedupeKey.Normalize("Acme Corp", "Backend Engineer")));
    }
}

public class ApplicationLedgerBatchTests : IDisposable
{
    private readonly TempDir _dir = new();

    private string LedgerPath => _dir.Path("applications.json");

    public void Dispose() => _dir.Dispose();

    private static ApplicationRecord Record(int i, string outcome = ApplicationOutcomes.Pending, DateTimeOffset? at = null) =>
        new(PostingIdentity.Key("Acme", $"Job {i}"), "Acme", $"Job {i}", $"https://x/{i}", at ?? DateTimeOffset.UtcNow, outcome);

    [Fact]
    public void InsideABatch_WritesEveryNRecords_AndTheRestWhenItEnds()
    {
        var ledger = new ApplicationLedger.ApplicationLedger(LedgerPath);

        using (ledger.BeginBatch(batchSize: 3))
        {
            ledger.RecordOutcome(Record(1));
            ledger.RecordOutcome(Record(2));
            Assert.False(File.Exists(LedgerPath));

            ledger.RecordOutcome(Record(3));
            Assert.Equal(3, new ApplicationLedger.ApplicationLedger(LedgerPath).Pending().Count);

            ledger.RecordOutcome(Record(4));
            Assert.Equal(3, new ApplicationLedger.ApplicationLedger(LedgerPath).Pending().Count);
        }

        Assert.Equal(4, new ApplicationLedger.ApplicationLedger(LedgerPath).Pending().Count);
        Assert.Equal(2, ledger.WriteCount);
        Assert.Empty(Directory.GetFiles(_dir.Root, "*.tmp*"));
    }

    [Fact]
    public void OutsideABatch_EveryRecordIsWrittenAtOnce()
    {
        var ledger = new ApplicationLedger.ApplicationLedger(LedgerPath);

        ledger.RecordOutcome(Record(1));
        ledger.RecordOutcome(Record(2));

        Assert.Equal(2, ledger.WriteCount);
        Assert.Equal(2, new ApplicationLedger.ApplicationLedger(LedgerPath).Pending().Count);
    }

    [Fact]
    public void ADecision_DuringABatch_IsWrittenAtOnce()
    {
        var ledger = new ApplicationLedger.ApplicationLedger(LedgerPath);
        ledger.RecordOutcome(Record(1));
        var postingId = ledger.Pending().Single().PostingId!;

        using (ledger.BeginBatch(batchSize: 100))
        {
            ledger.RecordOutcome(Record(2));
            ledger.Decide(postingId, ApplicationOutcomes.Applied, "user");

            var reloaded = new ApplicationLedger.ApplicationLedger(LedgerPath);
            Assert.Equal(ApplicationOutcomes.Applied, reloaded.Current(postingId)!.Outcome);
        }
    }

    [Fact]
    public void Current_IsTheLatestByTime_LaterAppendedWinningTies()
    {
        var ledger = new ApplicationLedger.ApplicationLedger(LedgerPath);
        var now = DateTimeOffset.UtcNow;
        ledger.RecordOutcome(Record(1, ApplicationOutcomes.Pending, now));
        ledger.RecordOutcome(Record(1, ApplicationOutcomes.Rejected, now.AddMinutes(-5)));
        var postingId = PostingIdentity.Id(PostingIdentity.Key("Acme", "Job 1"));

        Assert.Equal(ApplicationOutcomes.Pending, ledger.Current(postingId)!.Outcome);

        ledger.RecordOutcome(Record(1, ApplicationOutcomes.Applied, now));
        Assert.Equal(ApplicationOutcomes.Applied, ledger.Current(postingId)!.Outcome);
        Assert.Empty(ledger.Pending());
        Assert.Equal(3, ledger.History(postingId).Count);
    }

    [Fact]
    public void Lookups_StayFast_OnALargeLedger()
    {
        var ledger = new ApplicationLedger.ApplicationLedger(LedgerPath);
        const int count = 20_000;
        using (ledger.BeginBatch(batchSize: count))
        {
            for (var i = 0; i < count; i++)
                ledger.RecordOutcome(Record(i));
        }

        var ids = Enumerable.Range(0, count).Select(i => PostingIdentity.Id(PostingIdentity.Key("Acme", $"Job {i}"))).ToList();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        foreach (var id in ids)
            Assert.NotNull(ledger.Current(id));
        for (var i = 0; i < count; i++)
            Assert.True(ledger.HasBeenProcessed(PostingIdentity.Key("Acme", $"Job {i}")));
        watch.Stop();

        Assert.Equal(1, ledger.WriteCount);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"Lookups took {watch.Elapsed}.");
    }
}
