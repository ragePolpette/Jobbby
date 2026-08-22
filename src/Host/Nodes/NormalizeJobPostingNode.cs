using System.Text.Json;
using System.Text.Json.Serialization;
using GraphEngine;
using JobPostings;

namespace Host.Nodes;

/// <summary>
/// Turns a RawPosting into a normalized JobPosting. Company, SeniorityLevel and
/// RequiredStack come from the LLM reading the raw title/description - Company is
/// included in that same extraction because RawPosting doesn't carry it separately.
/// ApplyChannel is decided deterministically, no LLM involved: a "mailto:" ApplyUrl is
/// email; an ApplyUrl on the source's own domain is native_form; anything else is
/// external_platform.
/// </summary>
public sealed class NormalizeJobPostingNode : INode
{
    public const string RawPostingStateKey = "RawPosting";
    public const string JobPostingStateKey = "JobPosting";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly ILlmClient _llmClient;

    public NormalizeJobPostingNode(ILlmClient llmClient)
    {
        _llmClient = llmClient;
    }

    public async Task<NodeResult> ExecuteAsync(GraphState state)
    {
        var rawPosting = state.Get<RawPosting>(RawPostingStateKey)
            ?? throw new InvalidOperationException($"No RawPosting found in state under '{RawPostingStateKey}'.");

        var sourceUrl = state.Get<string>(JobApplicationStateKeys.SourceUrl) ?? string.Empty;

        var extraction = await ExtractAsync(rawPosting).ConfigureAwait(false);
        var applyChannel = DetermineApplyChannel(rawPosting.ApplyUrl, rawPosting.SourceDomain);

        var jobPosting = new JobPosting(
            Title: rawPosting.RawTitle,
            Company: extraction.Company,
            SeniorityLevel: extraction.SeniorityLevel,
            RequiredStack: extraction.RequiredStack,
            Description: rawPosting.RawDescription,
            SourceUrl: sourceUrl,
            ApplyUrl: rawPosting.ApplyUrl,
            ApplyChannel: applyChannel);

        return NodeResult.From(new Dictionary<string, object>
        {
            [JobPostingStateKey] = jobPosting,
            // Kept in sync so the rest of the graph (AskApproval, RecordIfApproved) can
            // keep reading these flat keys unchanged.
            [JobApplicationStateKeys.Company] = jobPosting.Company,
            [JobApplicationStateKeys.Title] = jobPosting.Title,
        });
    }

    internal static string DetermineApplyChannel(string applyUrl, string sourceDomain)
    {
        if (applyUrl.Contains("mailto:", StringComparison.OrdinalIgnoreCase))
            return ApplyChannels.Email;

        if (Uri.TryCreate(applyUrl, UriKind.Absolute, out var uri) &&
            string.Equals(uri.Host, sourceDomain, StringComparison.OrdinalIgnoreCase))
        {
            return ApplyChannels.NativeForm;
        }

        return ApplyChannels.ExternalPlatform;
    }

    private async Task<ExtractionResult> ExtractAsync(RawPosting rawPosting)
    {
        var prompt = $$"""
            Estrai le seguenti informazioni dall'annuncio di lavoro grezzo.

            Titolo: {{rawPosting.RawTitle}}
            Descrizione: {{rawPosting.RawDescription}}

            Restituisci SOLO JSON valido con questo schema, nessun markdown, nessun commento:
            {
              "company": string,
              "seniorityLevel": string,
              "requiredStack": [string]
            }
            """;

        var response = await _llmClient.CompleteAsync(prompt).ConfigureAwait(false);

        return JsonSerializer.Deserialize<ExtractionResult>(response, JsonOptions)
            ?? throw new InvalidOperationException("LLM response could not be parsed as job posting extraction JSON.");
    }

    private sealed record ExtractionResult
    {
        [JsonPropertyName("company")]
        public string Company { get; init; } = string.Empty;

        [JsonPropertyName("seniorityLevel")]
        public string SeniorityLevel { get; init; } = string.Empty;

        [JsonPropertyName("requiredStack")]
        public List<string> RequiredStack { get; init; } = new();
    }
}
