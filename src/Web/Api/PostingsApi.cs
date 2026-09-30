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

        app.MapPost("/api/postings/{postingId}/decision", async (string postingId, DecisionRequest request, LedgerHolder ledgers, SettingsService settings, PostingService postings, CancellationToken cancellationToken) =>
        {
            if (request.Decision is null || !Decisions.TryGetValue(request.Decision, out var outcome))
                return Results.Json(new { errors = new[] { new { field = "decision", message = "Decisione non valida: usa approve, reject o applied." } } }, statusCode: 400);

            var current = settings.Load();
            var decided = ledgers.Get(current.Dedupe.ExtraCompanySuffixes).Decide(postingId, outcome, $"Deciso dall'utente: {request.Decision}.");
            if (decided is null)
                return Results.NotFound(new { error = "Annuncio sconosciuto." });

            // Approving proposes the message; the decision stands even if writing it fails.
            string? presentationError = null;
            if (outcome == ApplicationOutcomes.Approved && current.Presentation.Enabled)
            {
                try
                {
                    decided = await postings.GeneratePresentationAsync(new PostingTarget.Ledger(postingId), cancellationToken);
                }
                catch (Exception ex) when (ex is PostingActionException or Host.PresentationException)
                {
                    presentationError = ex.Message;
                }
            }

            return Results.Json(new { record = decided, presentationError });
        });
    }
}
