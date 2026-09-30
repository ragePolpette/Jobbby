using ApplicationLedger;

namespace Web.Api;

public static class PostingsApi
{
    private static readonly IReadOnlyDictionary<string, string> Decisions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["approve"] = ApplicationOutcomes.Approved,
        ["reject"] = ApplicationOutcomes.Rejected,
        ["applied"] = ApplicationOutcomes.Applied,
    };

    public sealed record DecisionRequest(string? Decision);

    public static void MapPostingsApi(this WebApplication app)
    {
        app.MapGet("/api/pending", (LedgerHolder ledgers, SettingsService settings) =>
            Results.Json(ledgers.Get(settings.Load().Dedupe.ExtraCompanySuffixes).Pending()));

        app.MapGet("/api/postings/{postingId}", (string postingId, LedgerHolder ledgers, SettingsService settings) =>
        {
            var ledger = ledgers.Get(settings.Load().Dedupe.ExtraCompanySuffixes);
            return ledger.Current(postingId) is { } current
                ? Results.Json(new { current, history = ledger.History(postingId) })
                : Results.NotFound(new { error = "Annuncio sconosciuto." });
        });

        app.MapPost("/api/postings/{postingId}/decision", (string postingId, DecisionRequest request, LedgerHolder ledgers, SettingsService settings) =>
        {
            if (request.Decision is null || !Decisions.TryGetValue(request.Decision, out var outcome))
                return Results.Json(new { errors = new[] { new { field = "decision", message = "Decisione non valida: usa approve, reject o applied." } } }, statusCode: 400);

            var decided = ledgers.Get(settings.Load().Dedupe.ExtraCompanySuffixes).Decide(postingId, outcome, $"Deciso dall'utente: {request.Decision}.");
            return decided is null ? Results.NotFound(new { error = "Annuncio sconosciuto." }) : Results.Json(decided);
        });
    }
}
