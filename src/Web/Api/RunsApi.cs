using System.Text.Json;
using System.Text.Json.Serialization;
using ApplicationLedger;
using Host;

namespace Web.Api;

public static class RunsApi
{
    private static readonly JsonSerializerOptions EventJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public sealed record StartRequest(string? Mode);

    public static void MapRunsApi(this WebApplication app)
    {
        app.MapPost("/api/runs", (StartRequest request, RunManager runs) =>
        {
            RunMode mode;
            switch (request.Mode?.Trim().ToLowerInvariant())
            {
                case "dry": mode = RunMode.Dry; break;
                case "normal": mode = RunMode.Normal; break;
                default:
                    return Results.Json(new { errors = new[] { new { field = "mode", message = "Modalità non valida: usa \"dry\" o \"normal\"." } } }, statusCode: 400);
            }

            return runs.Start(mode) switch
            {
                StartResult.Started started => Results.Json(Describe(started.Run), statusCode: StatusCodes.Status202Accepted),
                StartResult.AlreadyRunning running => Results.Json(new { error = "C'è già una run in corso.", current = Describe(running.Run) }, statusCode: StatusCodes.Status409Conflict),
                StartResult.Invalid invalid => Results.Json(new { errors = invalid.Errors.Select(e => new { field = e.Field, message = e.Message }) }, statusCode: 400),
                _ => Results.StatusCode(500),
            };
        });

        app.MapGet("/api/runs/current", (RunManager runs) =>
            runs.Current is { } run ? Results.Json(Describe(run, withEvents: true)) : Results.NoContent());

        app.MapPost("/api/runs/current/cancel", (RunManager runs) =>
            runs.Cancel() ? Results.Accepted() : Results.Json(new { error = "Nessuna run in corso." }, statusCode: 409));

        app.MapGet("/api/runs/current/events", async (HttpContext context, RunManager runs) =>
        {
            var run = runs.Current;
            if (run is null)
            {
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                return;
            }

            context.Response.Headers.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-cache";
            var sent = 0;
            var aborted = context.RequestAborted;
            while (!aborted.IsCancellationRequested)
            {
                var (events, finished) = run.Read(sent);
                foreach (var runEvent in events)
                    await context.Response.WriteAsync($"data: {JsonSerializer.Serialize(runEvent, EventJson)}\n\n", aborted);
                sent += events.Count;
                await context.Response.Body.FlushAsync(aborted);

                if (finished && run.Read(sent).Events.Count == 0)
                {
                    await context.Response.WriteAsync($"event: end\ndata: {JsonSerializer.Serialize(Describe(run), EventJson)}\n\n", aborted);
                    await context.Response.Body.FlushAsync(aborted);
                    return;
                }

                try
                {
                    await Task.Delay(250, aborted);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        });

        app.MapGet("/api/runs", (RunStore store) =>
            Results.Json(store.List().Select(run => new
            {
                run.RunId,
                run.Mode,
                run.Status,
                run.StartedAt,
                run.EndedAt,
                run.AdzunaCalls,
                Postings = run.Postings.Count,
                run.Report,
                run.Warnings,
                run.Error,
                Where = run.Area?.Where,
            }), EventJson));

        app.MapGet("/api/runs/{runId}", (string runId, RunStore store, LedgerHolder ledgers, SettingsService settings) =>
        {
            if (store.Load(runId) is not { } run)
                return Results.NotFound(new { error = "Run sconosciuta." });

            // Resolve each posting by recomputing its key with today's settings: a stored PostingId
            // may predate a change of the company-suffix settings.
            var suffixes = settings.Load().Dedupe.ExtraCompanySuffixes;
            var ledger = ledgers.Get(suffixes);
            var postings = run.Postings.Select(posting =>
            {
                var id = PostingIdentity.Id(PostingIdentity.Key(posting.Record?.Company ?? posting.Company, posting.Record?.Title ?? posting.Title, suffixes, posting.ApplyUrl));
                var current = ledger.Current(id);
                return new { posting, currentPostingId = current?.PostingId, currentOutcome = current?.Outcome, currentRecord = current };
            });
            return Results.Json(new { run, postings }, EventJson);
        });
    }

    private static object Describe(ActiveRun run, bool withEvents = false) => new
    {
        runId = run.RunId,
        mode = run.Mode == RunMode.Dry ? "dry" : "normal",
        status = run.Status,
        startedAt = run.StartedAt,
        error = run.Error,
        report = run.Summary?.Report,
        adzunaCalls = run.Summary?.AdzunaCalls,
        events = withEvents ? run.Read(0).Events : null,
    };
}
