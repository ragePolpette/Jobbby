using System.Text.Json.Serialization;
using Config;

namespace Host;

/// <summary>
/// The settings a run is recorded with, copied section by section from an explicit allowlist:
/// only what shapes the search and the evaluation. Anything else in <see cref="JobbbySettings"/>,
/// present or future (the presentation texts may carry contact details), stays out of
/// <c>runs/*.json</c>. Older run files, which stored every setting, still load.
/// </summary>
public sealed record RunSettingsSnapshot
{
    [JsonPropertyName("searches")]
    public SearchesSettings Searches { get; init; } = new();

    [JsonPropertyName("area")]
    public AreaSettings Area { get; init; } = new();

    [JsonPropertyName("salary")]
    public SalarySettings Salary { get; init; } = new();

    [JsonPropertyName("evaluation")]
    public EvaluationSettings Evaluation { get; init; } = new();

    [JsonPropertyName("llm")]
    public RunLlmSettings Llm { get; init; } = new();

    [JsonPropertyName("dryRun")]
    public DryRunSettings DryRun { get; init; } = new();

    [JsonPropertyName("remoteSweep")]
    public RemoteSweepSettings RemoteSweep { get; init; } = new();

    [JsonPropertyName("dedupe")]
    public DedupeSettings Dedupe { get; init; } = new();

    public static RunSettingsSnapshot From(JobbbySettings settings) => new()
    {
        Searches = settings.Searches,
        Area = settings.Area,
        Salary = settings.Salary,
        Evaluation = settings.Evaluation,
        Llm = new RunLlmSettings { Provider = settings.Llm.Provider, Model = settings.Llm.Model },
        DryRun = settings.DryRun,
        RemoteSweep = settings.RemoteSweep,
        Dedupe = settings.Dedupe,
    };
}

public sealed record RunLlmSettings
{
    [JsonPropertyName("provider")]
    public string Provider { get; init; } = string.Empty;

    [JsonPropertyName("model")]
    public string? Model { get; init; }
}
