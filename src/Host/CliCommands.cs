using System.Globalization;
using ApplicationLedger;

namespace Host;

/// <summary>
/// Minimal decision commands, so asynchronous outcomes can be handled without the web UI:
/// <c>pending</c> lists what waits for a decision, <c>decide &lt;postingId&gt; approve|reject|applied</c> records one.
/// </summary>
public static class CliCommands
{
    private static readonly IReadOnlyDictionary<string, string> Actions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["approve"] = ApplicationOutcomes.Approved,
        ["reject"] = ApplicationOutcomes.Rejected,
        ["applied"] = ApplicationOutcomes.Applied,
    };

    public static int Pending(DataDir dataDir, TextWriter output, IEnumerable<string>? extraCompanySuffixes = null)
    {
        var pending = new ApplicationLedger.ApplicationLedger(dataDir.ApplicationsPath, extraCompanySuffixes).Pending();
        if (pending.Count == 0)
        {
            output.WriteLine("Nessun annuncio da decidere.");
            return 0;
        }

        foreach (var record in pending)
        {
            var confidence = record.Confidence?.ToString("0.00", CultureInfo.InvariantCulture) ?? "-";
            output.WriteLine($"{record.PostingId}  {confidence}  {record.Title} · {record.Company}  {record.ApplyUrl ?? record.SourceUrl}");
            if (!string.IsNullOrWhiteSpace(record.Reason))
                output.WriteLine($"    {record.Reason}");
        }

        output.WriteLine($"{pending.Count} da decidere. Usa: decide <postingId> approve|reject|applied");
        return 0;
    }

    public static int Decide(DataDir dataDir, string postingId, string action, TextWriter output, TextWriter error, IEnumerable<string>? extraCompanySuffixes = null)
    {
        if (!Actions.TryGetValue(action, out var outcome))
        {
            error.WriteLine($"Azione sconosciuta: '{action}'. Valori ammessi: {string.Join(", ", Actions.Keys)}.");
            return 2;
        }

        var ledger = new ApplicationLedger.ApplicationLedger(dataDir.ApplicationsPath, extraCompanySuffixes);
        var decided = ledger.Decide(postingId, outcome, $"Deciso dall'utente: {action}.");
        if (decided is null)
        {
            error.WriteLine($"Annuncio sconosciuto: '{postingId}'. Usa 'pending' per vedere gli id.");
            return 2;
        }

        output.WriteLine($"{decided.Title} · {decided.Company}: {outcome}.");
        return 0;
    }
}
