namespace Config;

/// <summary>One invalid setting; <see cref="Field"/> is its dotted JSON path, e.g. "area.distanceKm".</summary>
public sealed record SettingsError(string Field, string Message);

public sealed class SettingsValidationException(IReadOnlyList<SettingsError> errors)
    : Exception("Impostazioni non valide: " + string.Join("; ", errors.Select(e => $"{e.Field}: {e.Message}")))
{
    public IReadOnlyList<SettingsError> Errors { get; } = errors;
}

public static class SettingsValidator
{
    public static readonly IReadOnlyList<string> LlmProviders = new[] { "openai", "claude-cli" };

    /// <param name="forRun">Adds the checks that only matter when starting a run (e.g. the country).</param>
    public static IReadOnlyList<SettingsError> Validate(JobbbySettings settings, bool forRun)
    {
        var errors = new List<SettingsError>();
        void Error(string field, string message) => errors.Add(new SettingsError(field, message));

        var area = settings.Area;
        if (area.Country is null)
        {
            if (forRun)
                Error("area.country", "Scegli il paese in cui cercare.");
        }
        else if (!AdzunaCountries.IsSupported(area.Country))
        {
            Error("area.country", $"Paese non supportato: '{area.Country}'. Valori ammessi: {string.Join(", ", AdzunaCountries.CurrencyByCode.Keys)}.");
        }

        if (area.DistanceKm is < 0)
            Error("area.distanceKm", "Il raggio non può essere negativo.");
        else if (area.DistanceKm is not null && string.IsNullOrWhiteSpace(area.Where))
            Error("area.distanceKm", "Il raggio richiede una località.");

        var searches = settings.Searches;
        if (!searches.DeriveFromCv && searches.Queries.All(string.IsNullOrWhiteSpace))
            Error("searches.queries", "Aggiungi almeno una ricerca o attiva le ricerche ricavate dal CV.");
        if (searches.MaxDerivedQueries is < 1 or > 10)
            Error("searches.maxDerivedQueries", "Deve essere tra 1 e 10.");

        if (settings.Salary.MinimumYearly is < 0)
            Error("salary.minimumYearly", "La retribuzione minima non può essere negativa.");
        if (settings.Salary.MinimumPlausible < 0)
            Error("salary.minimumPlausible", "La soglia di plausibilità non può essere negativa.");

        if (settings.Evaluation.AutoApproveThreshold is < 0 or > 1)
            Error("evaluation.autoApproveThreshold", "Deve essere tra 0 e 1.");

        if (settings.DryRun.MaxPostingsPerQuery is < 1 or > 20)
            Error("dryRun.maxPostingsPerQuery", "Deve essere tra 1 e 20.");

        if (!LlmProviders.Contains(settings.Llm.Provider))
            Error("llm.provider", $"Provider non supportato: '{settings.Llm.Provider}'. Valori ammessi: {string.Join(", ", LlmProviders)}.");

        return errors;
    }
}
