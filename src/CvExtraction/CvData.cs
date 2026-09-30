using System.Text.Json.Serialization;

namespace CvExtraction;

// Property-based (not positional) so it has a parameterless constructor and settable
// properties: YamlDotNet's default object factory needs both, System.Text.Json is fine
// with either style.

/// <summary>One role within a CV's work history.</summary>
public sealed record CvRole
{
    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("company")]
    public string Company { get; init; } = string.Empty;

    [JsonPropertyName("stack")]
    public List<string> Stack { get; init; } = new();

    [JsonPropertyName("highlights")]
    public List<string> Highlights { get; init; } = new();
}

/// <summary>
/// The CV schema produced by the extraction pipeline (see <see cref="CvExtractionPrompt"/>) -
/// the same shape whether it came from an LLM reading a PDF, or was loaded directly from
/// a hand-written JSON/YAML file via <see cref="CvLoader"/>.
/// </summary>
public sealed record CvData
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("yearsExperience")]
    public double YearsExperience { get; init; }

    [JsonPropertyName("seniority")]
    public string Seniority { get; init; } = string.Empty;

    [JsonPropertyName("roles")]
    public List<CvRole> Roles { get; init; } = new();

    [JsonPropertyName("skills")]
    public List<string> Skills { get; init; } = new();

    [JsonPropertyName("languages")]
    public List<string> Languages { get; init; } = new();
}
