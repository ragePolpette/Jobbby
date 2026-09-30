using System.Text.Json.Serialization;

namespace Config;

/// <summary>
/// Every non-secret setting of a Jobbby installation, persisted as <c>settings.json</c> in
/// the DataDir (see <see cref="SettingsStore"/>). <see cref="Default"/> is deliberately
/// neutral: no profession, country or search of any particular user lives in code.
/// </summary>
public sealed record JobbbySettings
{
    public static JobbbySettings Default { get; } = new();

    [JsonPropertyName("searches")]
    public SearchesSettings Searches { get; init; } = new();

    [JsonPropertyName("area")]
    public AreaSettings Area { get; init; } = new();

    [JsonPropertyName("salary")]
    public SalarySettings Salary { get; init; } = new();

    [JsonPropertyName("evaluation")]
    public EvaluationSettings Evaluation { get; init; } = new();

    [JsonPropertyName("llm")]
    public LlmSettings Llm { get; init; } = new();

    [JsonPropertyName("dryRun")]
    public DryRunSettings DryRun { get; init; } = new();

    [JsonPropertyName("remoteSweep")]
    public RemoteSweepSettings RemoteSweep { get; init; } = new();

    [JsonPropertyName("dedupe")]
    public DedupeSettings Dedupe { get; init; } = new();

    [JsonPropertyName("presentation")]
    public PresentationSettings Presentation { get; init; } = new();
}

public sealed record SearchesSettings
{
    [JsonPropertyName("queries")]
    public List<string> Queries { get; init; } = new();

    [JsonPropertyName("deriveFromCv")]
    public bool DeriveFromCv { get; init; } = true;

    [JsonPropertyName("maxDerivedQueries")]
    public int MaxDerivedQueries { get; init; } = 3;
}

public sealed record AreaSettings
{
    /// <summary>Adzuna country code (see <see cref="AdzunaCountries"/>); required before a run.</summary>
    [JsonPropertyName("country")]
    public string? Country { get; init; }

    [JsonPropertyName("where")]
    public string Where { get; init; } = string.Empty;

    [JsonPropertyName("distanceKm")]
    public int? DistanceKm { get; init; }

    [JsonPropertyName("acceptsRemote")]
    public bool AcceptsRemote { get; init; }
}

public sealed record SalarySettings
{
    /// <summary>In the country's currency. Null = no salary filter.</summary>
    [JsonPropertyName("minimumYearly")]
    public decimal? MinimumYearly { get; init; }

    /// <summary>Reported salaries below this are not treated as yearly figures and count as unknown.</summary>
    [JsonPropertyName("minimumPlausible")]
    public decimal MinimumPlausible { get; init; } = 5000m;
}

public sealed record EvaluationSettings
{
    [JsonPropertyName("autoApproveThreshold")]
    public double AutoApproveThreshold { get; init; } = 0.7;
}

public sealed record LlmSettings
{
    [JsonPropertyName("provider")]
    public string Provider { get; init; } = "claude-cli";

    [JsonPropertyName("model")]
    public string? Model { get; init; } = "sonnet";
}

public sealed record DryRunSettings
{
    [JsonPropertyName("maxPostingsPerQuery")]
    public int MaxPostingsPerQuery { get; init; } = 3;
}

public sealed record RemoteSweepSettings
{
    /// <summary>Keywords per language code; empty = the remote sweep stays off.</summary>
    [JsonPropertyName("keywords")]
    public Dictionary<string, List<string>> Keywords { get; init; } = new();

    public IReadOnlyList<string> AllKeywords() =>
        Keywords.Values
            .SelectMany(words => words)
            .Select(word => word.Trim())
            .Where(word => word.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}

public sealed record DedupeSettings
{
    [JsonPropertyName("extraCompanySuffixes")]
    public List<string> ExtraCompanySuffixes { get; init; } = new();
}

public sealed record PresentationSettings
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; } = true;

    /// <summary>Empty = the LLM writes a neutral opening in the chosen language.</summary>
    [JsonPropertyName("opening")]
    public string Opening { get; init; } = string.Empty;

    /// <summary>Inserted verbatim: the LLM never writes contact details.</summary>
    [JsonPropertyName("closing")]
    public string Closing { get; init; } = string.Empty;

    [JsonPropertyName("tone")]
    public string Tone { get; init; } = "formale";

    [JsonPropertyName("length")]
    public string Length { get; init; } = "breve";

    /// <summary>"annuncio" = the posting's language, otherwise a language code.</summary>
    [JsonPropertyName("language")]
    public string Language { get; init; } = "annuncio";

    [JsonPropertyName("extraInstructions")]
    public string ExtraInstructions { get; init; } = string.Empty;
}
