using System.Text.Json;
using Config;

namespace Web.Api;

public static class SettingsApi
{
    /// <summary>Secrets whose presence the UI shows; their values never leave the server.</summary>
    private static readonly string[] SourceSecretKeys = { "Adzuna:AppId", "Adzuna:AppKey" };

    /// <summary>Only the openai provider needs an endpoint and a key; claude-cli uses the local login.</summary>
    private static readonly string[] OpenAiSecretKeys = { "Llm:Endpoint", "Llm:ApiKey" };

    public static void MapSettingsApi(this WebApplication app)
    {
        app.MapGet("/api/settings", (SettingsService settings) => Results.Json(settings.Load()));

        app.MapPut("/api/settings", async (HttpRequest request, SettingsService settings) =>
        {
            JobbbySettings? candidate;
            try
            {
                candidate = await JsonSerializer.DeserializeAsync<JobbbySettings>(request.Body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (JsonException ex)
            {
                return ValidationProblem(new SettingsError(ex.Path?.TrimStart('$', '.') ?? "", $"Valore non valido: {ex.Message}"));
            }

            if (candidate is null)
                return ValidationProblem(new SettingsError("", "Impostazioni mancanti."));

            var nulls = SettingsStore.NullFields(candidate);
            var errors = nulls.Select(field => new SettingsError(field, "Il campo non può essere null."))
                .Concat(nulls.Count == 0 ? SettingsValidator.Validate(candidate, forRun: false) : Array.Empty<SettingsError>())
                .ToList();
            if (errors.Count > 0)
                return ValidationProblem(errors.ToArray());

            settings.Save(candidate);
            return Results.Json(candidate);
        });

        app.MapGet("/api/secrets/status", (IConfiguration configuration, SettingsService settings) =>
        {
            var openAi = string.Equals(settings.Load().Llm.Provider?.Trim(), Host.LlmClientFactory.OpenAiProvider, StringComparison.OrdinalIgnoreCase);
            var keys = openAi ? SourceSecretKeys.Concat(OpenAiSecretKeys) : SourceSecretKeys;
            return Results.Json(keys.ToDictionary(key => key, key => !string.IsNullOrWhiteSpace(configuration[key])));
        });
    }

    private static IResult ValidationProblem(params SettingsError[] errors) =>
        Results.Json(new { errors = errors.Select(e => new { field = e.Field, message = e.Message }) }, statusCode: StatusCodes.Status400BadRequest);
}
