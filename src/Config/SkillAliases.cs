using System.Text.Json;
using System.Text.Json.Serialization;

namespace Config;

/// <summary>
/// Profession-specific skill equivalences, kept as data in <c>DataDir/skill-aliases.json</c>
/// rather than in code: <see cref="Aliases"/> maps a spelling to its canonical form ("k8s" →
/// "kubernetes"), <see cref="Implies"/> says knowing one skill implies others ("bls-d" →
/// ["bls"]). No file means no aliases.
/// </summary>
public sealed record SkillAliases
{
    public static SkillAliases Empty { get; } = new();

    [JsonPropertyName("aliases")]
    public Dictionary<string, string> Aliases { get; init; } = new();

    [JsonPropertyName("implies")]
    public Dictionary<string, List<string>> Implies { get; init; } = new();

    public static SkillAliases Load(string path)
    {
        if (!File.Exists(path))
            return Empty;

        try
        {
            return JsonSerializer.Deserialize<SkillAliases>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? Empty;
        }
        catch (JsonException ex)
        {
            throw new SettingsFileException($"{path} non valido al campo '{ex.Path ?? "?"}': {ex.Message}", ex);
        }
    }
}
