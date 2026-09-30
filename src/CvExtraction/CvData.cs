using System.Text.Json.Serialization;
using YamlDotNet.Serialization;

namespace CvExtraction;

// Property-based (not positional) so it has a parameterless constructor and settable
// properties: YamlDotNet's default object factory needs both, System.Text.Json is fine
// with either style. Lists coalesce null to empty: hand-written or LLM-produced files
// may contain "skills": null.

/// <summary>One role within a CV's work history.</summary>
public sealed record CvRole
{
    private readonly List<string> _skills = new();
    private readonly List<string> _legacyStack = new();
    private readonly List<string> _highlights = new();

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("company")]
    public string Company { get; init; } = string.Empty;

    /// <summary>The role's skills, plus those of the legacy "stack" key whatever the key order.</summary>
    [JsonPropertyName("skills")]
    public List<string> Skills
    {
        get => _legacyStack.Count == 0 ? _skills : _skills.Concat(_legacyStack).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        init => _skills = value ?? new List<string>();
    }

    /// <summary>Older CVs named a role's skills "stack": read it, never write it.</summary>
    [JsonPropertyName("stack")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [YamlMember(Alias = "stack")]
    public List<string>? LegacyStack
    {
        get => null;
        init => _legacyStack = value ?? new List<string>();
    }

    [JsonPropertyName("highlights")]
    public List<string> Highlights
    {
        get => _highlights;
        init => _highlights = value ?? new List<string>();
    }
}

/// <summary>
/// The CV schema produced by the extraction pipeline (see <see cref="CvExtractionPrompt"/>) -
/// the same shape whether it came from an LLM reading a PDF, or was loaded directly from
/// a hand-written JSON/YAML file via <see cref="CvLoader"/>.
/// </summary>
public sealed record CvData
{
    private readonly List<CvRole> _roles = new();
    private readonly List<string> _skills = new();
    private readonly List<string> _languages = new();

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("yearsExperience")]
    public double YearsExperience { get; init; }

    [JsonPropertyName("seniority")]
    public string Seniority { get; init; } = string.Empty;

    [JsonPropertyName("roles")]
    public List<CvRole> Roles
    {
        get => _roles;
        init => _roles = value?.Where(role => role is not null).ToList() ?? new List<CvRole>();
    }

    [JsonPropertyName("skills")]
    public List<string> Skills
    {
        get => _skills;
        init => _skills = value ?? new List<string>();
    }

    [JsonPropertyName("languages")]
    public List<string> Languages
    {
        get => _languages;
        init => _languages = value ?? new List<string>();
    }

    /// <summary>Where the candidate lives (city, as written in the CV); the default search area.</summary>
    [JsonPropertyName("location")]
    public string? Location { get; init; }
}
