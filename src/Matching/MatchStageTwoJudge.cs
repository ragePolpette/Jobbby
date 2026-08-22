using System.Text.Json;
using System.Text.Json.Serialization;
using CvExtraction;
using GraphEngine;
using JobPostings;

namespace Matching;

/// <summary>
/// Second-stage judgment for postings that passed <see cref="MatchStageOneFilter"/>:
/// asks the LLM for a categorical verdict (Strong/Borderline/Weak) plus reasoning, and a
/// Confidence in that verdict kept explicitly separate from it - not a single blended
/// score.
/// </summary>
public sealed class MatchStageTwoJudge
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly ILlmClient _llmClient;

    public MatchStageTwoJudge(ILlmClient llmClient)
    {
        _llmClient = llmClient;
    }

    public async Task<MatchJudgment> JudgeAsync(JobPosting posting, CvData cv, CancellationToken cancellationToken = default)
    {
        var prompt = BuildPrompt(posting, cv);
        var response = await _llmClient.CompleteAsync(prompt, cancellationToken).ConfigureAwait(false);

        var result = JsonSerializer.Deserialize<LlmJudgmentResponse>(response, JsonOptions)
            ?? throw new InvalidOperationException("LLM response could not be parsed as a match judgment.");

        return new MatchJudgment(result.Category, result.Reasoning, result.Confidence);
    }

    private static string BuildPrompt(JobPosting posting, CvData cv)
    {
        var requiredStack = string.Join(", ", posting.RequiredStack);
        var candidateSkills = string.Join(", ", cv.Skills);

        return $$"""
            Valuta quanto il candidato è adatto per questo annuncio di lavoro.

            Annuncio:
            Titolo: {{posting.Title}}
            Azienda: {{posting.Company}}
            Seniority richiesta: {{posting.SeniorityLevel}}
            Stack richiesto: {{requiredStack}}
            Descrizione: {{posting.Description}}

            Candidato:
            Anni di esperienza: {{cv.YearsExperience}}
            Seniority: {{cv.Seniority}}
            Competenze: {{candidateSkills}}

            Esprimi un giudizio CATEGORICO ("Strong", "Borderline" o "Weak") su quanto il
            match è buono, una motivazione testuale, e una Confidence (0-1) su quanto sei
            sicuro del giudizio stesso. La Confidence è indipendente dalla categoria: un
            giudizio "Weak" può comunque avere Confidence alta se sei molto sicuro che il
            match sia scarso.

            Restituisci SOLO JSON valido con questo schema, nessun markdown, nessun commento:
            {
              "category": "Strong" | "Borderline" | "Weak",
              "reasoning": string,
              "confidence": number
            }
            """;
    }

    private sealed record LlmJudgmentResponse
    {
        [JsonPropertyName("category")]
        public string Category { get; init; } = string.Empty;

        [JsonPropertyName("reasoning")]
        public string Reasoning { get; init; } = string.Empty;

        [JsonPropertyName("confidence")]
        public double Confidence { get; init; }
    }
}
