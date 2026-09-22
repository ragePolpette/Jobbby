using System.Text.Json;

namespace Config;

public static class ApprovedSourceStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static IReadOnlyList<SourceDefinition> Load(string path) =>
        File.Exists(path)
            ? JsonSerializer.Deserialize<List<SourceDefinition>>(File.ReadAllText(path)) ?? new List<SourceDefinition>()
            : new List<SourceDefinition>();

    public static void SaveApproved(string path, IEnumerable<SourceDefinition> approvedSources)
    {
        var existing = Load(path).ToDictionary(source => NormalizeUrl(source.BaseUrl), StringComparer.OrdinalIgnoreCase);
        foreach (var source in approvedSources)
            existing[NormalizeUrl(source.BaseUrl)] = source;

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(path, JsonSerializer.Serialize(existing.Values.OrderBy(source => source.Name), JsonOptions));
    }

    private static string NormalizeUrl(string url) => url.Trim().TrimEnd('/');
}
