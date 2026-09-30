using System.Text.Json;
using System.Text.Json.Serialization;
using Config;
using CvExtraction;
using GraphEngine;

namespace Host;

public sealed record SearchPlan(IReadOnlyList<string> Queries, IReadOnlyList<string> ConfiguredQueries, IReadOnlyList<string> DerivedQueries, string? DerivationError);

/// <summary>
/// Turns <see cref="SearchSettings"/> into the final list of queries: the configured ones
/// plus, when enabled, the ones an LLM derives from the CV. Duplicates are removed
/// case-insensitively. A failed derivation is not fatal as long as configured queries remain.
/// </summary>
public static class SearchQueryPlanner
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static async Task<SearchPlan> PlanAsync(
        SearchSettings settings,
        CvData cv,
        ILlmClient llmClient,
        CancellationToken cancellationToken = default)
    {
        var configured = Distinct(settings.Queries);
        IReadOnlyList<string> derived = Array.Empty<string>();
        string? derivationError = null;

        if (settings.DeriveFromCv)
        {
            var maxDerived = Math.Clamp(settings.MaxDerivedQueries, 1, SearchSettings.MaxAllowedDerivedQueries);
            try
            {
                var response = await llmClient.CompleteAsync(BuildPrompt(cv, configured, maxDerived), cancellationToken).ConfigureAwait(false);
                var parsed = JsonSerializer.Deserialize<DerivedQueries>(response, JsonOptions)
                    ?? throw new InvalidOperationException("LLM response deserialized to null.");
                derived = Distinct(parsed.Queries).Take(maxDerived).ToList();
            }
            catch (Exception ex) when (ex is LlmException or JsonException or InvalidOperationException)
            {
                derivationError = ex.Message;
            }
        }

        var all = Distinct(configured.Concat(derived));
        if (all.Count == 0)
        {
            throw new InvalidOperationException(derivationError is null
                ? "No search queries configured. Add queries to searches.json or enable deriveFromCv."
                : $"No search queries configured and deriving them from the CV failed: {derivationError}");
        }

        return new SearchPlan(all, configured, derived, derivationError);
    }

    private static List<string> Distinct(IEnumerable<string> queries) =>
        queries
            .Select(query => query.Trim())
            .Where(query => query.Length > 0)
            .DistinctBy(query => query.ToLowerInvariant())
            .ToList();

    private static string BuildPrompt(CvData cv, IReadOnlyList<string> configured, int maxDerived)
    {
        var roles = cv.Roles.Count == 0
            ? "(nessuno)"
            : string.Join("\n", cv.Roles.Select(role => $"- {role.Title} presso {role.Company} ({string.Join(", ", role.Stack)})"));
        var alreadyConfigured = configured.Count == 0 ? "(nessuna)" : string.Join(", ", configured);

        return $$"""
            Sei un recruiter. Dal profilo del candidato ricava fino a {{maxDerived}} query di ricerca per un motore di annunci di lavoro.
            Ogni query è un titolo di ruolo breve (2-4 parole), come lo scriverebbe un'azienda in un annuncio, ad esempio "Backend Developer C#".
            Non ripetere query equivalenti a quelle già configurate: {{alreadyConfigured}}.

            Seniority: {{cv.Seniority}} ({{cv.YearsExperience}} anni di esperienza)
            Ruoli:
            {{roles}}
            Competenze: {{string.Join(", ", cv.Skills)}}

            Restituisci SOLO JSON valido con questo schema, nessun markdown, nessun commento:
            {
              "queries": [string]
            }
            """;
    }

    private sealed record DerivedQueries
    {
        [JsonPropertyName("queries")]
        public List<string> Queries { get; init; } = new();
    }
}
