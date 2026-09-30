using System.Text.Json;
using Config;
using CvExtraction;
using Host;

namespace Web.Api;

public static class CvApi
{
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    public static void MapCvApi(this WebApplication app)
    {
        app.MapGet("/api/cv", (CvStore store, DataDir dataDir, IConfiguration configuration) =>
            Describe(store.Read(), dataDir, configuration));

        // The form is read by hand: no automatic IFormFile binding (and its antiforgery requirement);
        // RequestGuard already protects every state-changing request.
        app.MapPost("/api/cv", async (HttpRequest request, CvStore store, CvGate gate, SettingsService settings,
            IRunDependenciesFactory dependencies, DataDir dataDir, IConfiguration configuration, CancellationToken cancellationToken) =>
        {
            if (request.ContentLength > CvStore.MaxUploadBytes + 64 * 1024)
                return TooLarge();
            if (!request.HasFormContentType)
                return Error(StatusCodes.Status400BadRequest, "Carica un file .pdf o .json.");

            IFormCollection form;
            try
            {
                form = await request.ReadFormAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException)
            {
                return Error(StatusCodes.Status400BadRequest, $"Caricamento non valido: {ex.Message}");
            }

            var file = form.Files["file"];
            if (file is null || file.Length == 0)
                return Error(StatusCodes.Status400BadRequest, "Nessun file ricevuto: scegli un .pdf o un .json.");
            if (file.Length > CvStore.MaxUploadBytes)
                return TooLarge();

            using var buffer = new MemoryStream();
            await file.CopyToAsync(buffer, cancellationToken);
            var content = buffer.ToArray();
            return await gate.RunAsync(async () =>
                Describe(await store.UploadAsync(content, dependencies.CreateLlm(settings.Load()), cancellationToken), dataDir, configuration));
        });

        app.MapPut("/api/cv", async (HttpRequest request, CvStore store, CvGate gate, DataDir dataDir, IConfiguration configuration) =>
        {
            CvData? cv;
            try
            {
                cv = await JsonSerializer.DeserializeAsync<CvData>(request.Body, ReadOptions);
            }
            catch (JsonException ex)
            {
                return ValidationProblem(new SettingsError(ex.Path?.TrimStart('$', '.') ?? "", $"Valore non valido: {ex.Message}"));
            }

            if (cv is null)
                return ValidationProblem(new SettingsError("", "CV mancante."));
            var errors = CvValidator.Validate(CvValidator.Normalize(cv));
            if (errors.Count > 0)
                return ValidationProblem(errors.ToArray());

            return await gate.RunAsync(() => Task.FromResult(Describe(store.Save(cv), dataDir, configuration)));
        });

        app.MapPost("/api/cv/extract", (CvStore store, CvGate gate, SettingsService settings,
            IRunDependenciesFactory dependencies, DataDir dataDir, IConfiguration configuration, CancellationToken cancellationToken) =>
            gate.RunAsync(async () =>
                Describe(await store.ExtractPdfAsync(dependencies.CreateLlm(settings.Load()), cancellationToken), dataDir, configuration)));

        // POST, not GET: every preview spends an LLM call, and a GET could be triggered by any page (an <img>)
        // without the RequestGuard header.
        app.MapPost("/api/cv/derived-queries", async (CvStore store, SettingsService settingsService,
            IRunDependenciesFactory dependencies, CancellationToken cancellationToken) =>
        {
            var cv = store.Read().Cv;
            if (cv is null)
                return Error(StatusCodes.Status400BadRequest, "Nessun CV strutturato: carica il CV o estrai il PDF.");

            var settings = settingsService.Load();
            var configured = settings.Searches.Queries.Select(q => q.Trim()).Where(q => q.Length > 0).ToList();
            if (!settings.Searches.DeriveFromCv)
                return Results.Json(new { enabled = false, configured, derived = Array.Empty<string>(), error = (string?)null });

            try
            {
                var plan = await SearchQueryPlanner.PlanAsync(
                    new SearchSettings
                    {
                        Queries = settings.Searches.Queries,
                        DeriveFromCv = true,
                        MaxDerivedQueries = settings.Searches.MaxDerivedQueries,
                    },
                    cv,
                    dependencies.CreateLlm(settings),
                    cancellationToken);
                return Results.Json(new { enabled = true, configured = plan.ConfiguredQueries, derived = plan.DerivedQueries, error = plan.DerivationError });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Json(new { enabled = true, configured, derived = Array.Empty<string>(), error = ex.Message });
            }
        });
    }

    private static IResult Describe(CvState state, DataDir dataDir, IConfiguration configuration)
    {
        var usedByRuns = CvLocator.Find(dataDir, configuration);
        return Results.Json(new
        {
            source = state.Source,
            cv = state.Cv,
            updatedAt = state.UpdatedAt,
            needsExtraction = state.NeedsExtraction,
            notes = state.Notes,
            usedByRuns = usedByRuns is null ? null : Path.GetFileName(usedByRuns),
            configuredPath = !string.IsNullOrWhiteSpace(configuration["Jobbby:CvPath"]),
        });
    }

    internal static IResult Error(int status, string message) => Results.Json(new { error = message }, statusCode: status);

    private static IResult TooLarge() =>
        Error(StatusCodes.Status413PayloadTooLarge, $"Il file supera il limite di {CvStore.MaxUploadBytes / (1024 * 1024)} MB.");

    private static IResult ValidationProblem(params SettingsError[] errors) =>
        Results.Json(new { errors = errors.Select(e => new { field = e.Field, message = e.Message }) }, statusCode: StatusCodes.Status400BadRequest);
}

/// <summary>
/// One CV change at a time (upload, extraction, save): a second one while an extraction is running gets
/// 409 instead of mixing files. Maps the CV errors to HTTP answers.
/// </summary>
public sealed class CvGate
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public async Task<IResult> RunAsync(Func<Task<IResult>> work)
    {
        if (!_semaphore.Wait(0))
            return CvApi.Error(StatusCodes.Status409Conflict, "C'è già un caricamento o un'estrazione del CV in corso.");
        try
        {
            return await work();
        }
        catch (CvFileException ex)
        {
            return CvApi.Error(StatusCodes.Status400BadRequest, ex.Message);
        }
        catch (CvExtractionException ex)
        {
            return CvApi.Error(StatusCodes.Status502BadGateway, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            // The LLM cannot be built from the settings (e.g. provider openai without Llm:Endpoint).
            return CvApi.Error(StatusCodes.Status400BadRequest, ex.Message);
        }
        finally
        {
            _semaphore.Release();
        }
    }
}
