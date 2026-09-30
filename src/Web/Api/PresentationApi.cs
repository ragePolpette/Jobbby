using System.Text.Json;
using Config;
using Host;

namespace Web.Api;

public static class PresentationApi
{
    public sealed record TextRequest(string? Text);

    public static void MapPresentationApi(this WebApplication app)
    {
        foreach (var (prefix, target) in Targets())
        {
            app.MapPost($"{prefix}/presentation", (HttpContext context, PostingService postings, CancellationToken cancellationToken) =>
                Handle(async () => Results.Json(await postings.GeneratePresentationAsync(target(context), cancellationToken))));

            app.MapPut($"{prefix}/presentation", (HttpContext context, TextRequest request, PostingService postings) =>
                Handle(() => Task.FromResult(Results.Json(postings.EditPresentation(target(context), request.Text)))));

            app.MapPut($"{prefix}/full-text", (HttpContext context, TextRequest request, PostingService postings, CancellationToken cancellationToken) =>
                Handle(async () => Results.Json(await postings.SetFullTextAsync(target(context), request.Text, cancellationToken))));
        }

        app.MapPost("/api/presentation/preview", async (HttpRequest request, PostingService postings, CancellationToken cancellationToken) =>
        {
            PreviewRequest? preview;
            try
            {
                preview = await JsonSerializer.DeserializeAsync<PreviewRequest>(request.Body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, cancellationToken);
            }
            catch (JsonException ex)
            {
                return ValidationProblem(new SettingsError("presentation", $"Valore non valido: {ex.Message}"));
            }

            if (preview?.Presentation is null)
                return ValidationProblem(new SettingsError("presentation", "Impostazioni del messaggio mancanti."));
            var errors = SettingsValidator.Validate(JobbbySettings.Default with { Presentation = preview.Presentation }, forRun: false);
            if (errors.Count > 0)
                return ValidationProblem(errors.ToArray());

            PostingTarget? target = preview.PostingId is null ? null
                : preview.RunId is null ? new PostingTarget.Ledger(preview.PostingId)
                : new PostingTarget.Run(preview.RunId, preview.PostingId);
            return await Handle(async () =>
            {
                var (message, posting) = await postings.PreviewAsync(preview.Presentation, target, cancellationToken);
                return Results.Json(new
                {
                    text = message.Text,
                    matches = message.Matches,
                    basedOnFullText = message.BasedOnFullText,
                    posting = new { title = posting.Title, company = posting.Company },
                });
            });
        });
    }

    /// <summary>Maps the posting errors (and a failed message) to HTTP answers.</summary>
    public static async Task<IResult> Handle(Func<Task<IResult>> work)
    {
        try
        {
            return await work();
        }
        catch (PostingActionException ex)
        {
            return Results.Json(new { error = ex.Message }, statusCode: ex.Status);
        }
        catch (PresentationException ex)
        {
            return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static IEnumerable<(string Prefix, Func<HttpContext, PostingTarget> Target)> Targets() => new (string, Func<HttpContext, PostingTarget>)[]
    {
        ("/api/postings/{postingId}", context => new PostingTarget.Ledger(Route(context, "postingId"))),
        ("/api/runs/{runId}/postings/{postingId}", context => new PostingTarget.Run(Route(context, "runId"), Route(context, "postingId"))),
    };

    private static string Route(HttpContext context, string name) => context.GetRouteValue(name)?.ToString() ?? string.Empty;

    private static IResult ValidationProblem(params SettingsError[] errors) =>
        Results.Json(new { errors = errors.Select(e => new { field = e.Field, message = e.Message }) }, statusCode: StatusCodes.Status400BadRequest);

    private sealed record PreviewRequest(PresentationSettings? Presentation, string? RunId, string? PostingId);
}
