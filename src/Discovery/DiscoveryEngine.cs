using System.Text.Json;
using System.Text.Json.Serialization;
using GraphEngine;

namespace Discovery;

/// <summary>
/// Finds and evaluates candidates for whatever a caller's <see cref="DiscoveryCriteria"/>
/// describes - this class has no built-in notion of what's being searched for. For each
/// candidate turned up by the initial intent search, a second search targets its
/// reputation specifically, and an LLM synthesizes both into a summary and reliability
/// score. Returns candidates only: no file writes, no whitelist concept, no filtering -
/// all of that is left to the caller.
/// </summary>
public sealed class DiscoveryEngine
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly IWebSearchClient _webSearchClient;
    private readonly ILlmClient _llmClient;

    public DiscoveryEngine(IWebSearchClient webSearchClient, ILlmClient llmClient)
    {
        _webSearchClient = webSearchClient;
        _llmClient = llmClient;
    }

    public async Task<IReadOnlyList<DiscoveryCandidate>> DiscoverAsync(
        DiscoveryCriteria criteria,
        CancellationToken cancellationToken = default)
    {
        var initialResults = await _webSearchClient
            .SearchAsync(criteria.SearchIntent, cancellationToken)
            .ConfigureAwait(false);

        var candidates = new List<DiscoveryCandidate>();

        foreach (var initialResult in initialResults)
        {
            var reputationQuery = $"{criteria.EvaluationCriteria} {initialResult.Title}";
            var reputationResults = await _webSearchClient
                .SearchAsync(reputationQuery, cancellationToken)
                .ConfigureAwait(false);

            var evaluation = await EvaluateAsync(initialResult, criteria, reputationResults, cancellationToken)
                .ConfigureAwait(false);

            candidates.Add(new DiscoveryCandidate(
                initialResult.Title,
                initialResult.Url,
                evaluation.EvaluationSummary,
                evaluation.ReliabilityScore));
        }

        return candidates;
    }

    private async Task<EvaluationResult> EvaluateAsync(
        SearchResult candidate,
        DiscoveryCriteria criteria,
        IReadOnlyList<SearchResult> reputationResults,
        CancellationToken cancellationToken)
    {
        var prompt = BuildEvaluationPrompt(candidate, criteria, reputationResults);
        var response = await _llmClient.CompleteAsync(prompt, cancellationToken).ConfigureAwait(false);

        return JsonSerializer.Deserialize<EvaluationResult>(response, JsonOptions)
            ?? throw new InvalidOperationException(
                $"LLM response for candidate '{candidate.Title}' could not be parsed as evaluation JSON.");
    }

    private static string BuildEvaluationPrompt(
        SearchResult candidate,
        DiscoveryCriteria criteria,
        IReadOnlyList<SearchResult> reputationResults)
    {
        var reputationSection = reputationResults.Count == 0
            ? "(nessun risultato)"
            : string.Join("\n", reputationResults.Select(r => $"- {r.Title}: {r.Snippet} ({r.Url})"));

        return $$"""
            Valuta l'affidabilità del candidato trovato tramite ricerca web, sulla base dei criteri forniti.

            Candidato: {{candidate.Title}}
            URL: {{candidate.Url}}
            Estratto iniziale: {{candidate.Snippet}}

            Criteri di valutazione: {{criteria.EvaluationCriteria}}

            Risultati della ricerca sulla reputazione:
            {{reputationSection}}

            Restituisci SOLO JSON valido con questo schema, nessun markdown, nessun commento:
            {
              "evaluationSummary": string,
              "reliabilityScore": number | null
            }
            """;
    }

    private sealed record EvaluationResult
    {
        [JsonPropertyName("evaluationSummary")]
        public string EvaluationSummary { get; init; } = string.Empty;

        [JsonPropertyName("reliabilityScore")]
        public int? ReliabilityScore { get; init; }
    }
}
