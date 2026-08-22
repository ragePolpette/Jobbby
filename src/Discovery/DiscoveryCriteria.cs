using System.Text.Json.Serialization;

namespace Discovery;

/// <summary>
/// What to search for and how to judge what's found. Loaded from a config file (JSON or
/// YAML, via <c>Config.ConfigLoader.Load&lt;DiscoveryCriteria&gt;(path)</c>) - the actual
/// values (what's being searched for, what "reliable" means) live only in the caller's
/// config, never in this project. Discovery has no idea what kind of things SearchIntent
/// describes - that's entirely up to the config.
/// </summary>
public sealed record DiscoveryCriteria
{
    [JsonPropertyName("searchIntent")]
    public string SearchIntent { get; init; } = string.Empty;

    [JsonPropertyName("evaluationCriteria")]
    public string EvaluationCriteria { get; init; } = string.Empty;
}
