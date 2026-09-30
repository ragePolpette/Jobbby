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

        SkillAliases? loaded;
        try
        {
            loaded = JsonSerializer.Deserialize<SkillAliases>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException ex)
        {
            throw new SettingsFileException($"{path} non valido al campo '{ex.Path ?? "?"}': {ex.Message}", ex);
        }

        if (loaded is null)
            return Empty;

        // System.Text.Json assigns explicit nulls; a null here would fail every posting at match time.
        var nulls = new List<string>();
        if (loaded.Aliases is null)
            nulls.Add("aliases");
        else
            nulls.AddRange(loaded.Aliases.Where(pair => pair.Value is null).Select(pair => $"aliases.{pair.Key}"));
        if (loaded.Implies is null)
            nulls.Add("implies");
        else
            nulls.AddRange(loaded.Implies.Where(pair => pair.Value is null || pair.Value.Any(skill => skill is null)).Select(pair => $"implies.{pair.Key}"));

        return nulls.Count == 0
            ? loaded
            : throw new SettingsFileException($"{path} non valido: questi campi non possono essere null: {string.Join(", ", nulls)}.");
    }
}
