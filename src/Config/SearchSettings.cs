using System.Text.Json.Serialization;

namespace Config;

/// <summary>
/// What to search for, independently of where (<see cref="SourceWhitelist"/>): every
/// query is run against every source. <see cref="DeriveFromCv"/> adds up to
/// <see cref="MaxDerivedQueries"/> queries an LLM infers from the candidate's CV.
/// </summary>
public sealed record SearchSettings
{
    public const int MaxAllowedDerivedQueries = 10;

    [JsonPropertyName("queries")]
    public List<string> Queries { get; init; } = new();

    [JsonPropertyName("deriveFromCv")]
    public bool DeriveFromCv { get; init; }

    [JsonPropertyName("maxDerivedQueries")]
    public int MaxDerivedQueries { get; init; } = 3;
}
