using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Config;

public sealed record SettingsLoadResult(JobbbySettings Settings, bool Created, IReadOnlyList<string> Notes);

public sealed class SettingsFileException(string message, Exception? innerException = null) : Exception(message, innerException);

/// <summary>Loads and saves <c>settings.json</c>. A missing file is created from the neutral defaults.</summary>
public static class SettingsStore
{
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <param name="legacySearchesPath">
    /// An existing <c>searches.json</c> to seed the searches from when settings.json is created;
    /// ignored if null, missing, or once settings.json exists.
    /// </param>
    public static SettingsLoadResult LoadOrCreate(string settingsPath, string? legacySearchesPath)
    {
        if (!File.Exists(settingsPath))
        {
            var settings = JobbbySettings.Default;
            var notes = new List<string>();
            if (legacySearchesPath is not null && File.Exists(legacySearchesPath))
            {
                var legacy = ConfigLoader.Load<SearchSettings>(legacySearchesPath);
                settings = settings with
                {
                    Searches = new SearchesSettings
                    {
                        Queries = legacy.Queries.ToList(),
                        DeriveFromCv = legacy.DeriveFromCv,
                        MaxDerivedQueries = legacy.MaxDerivedQueries,
                    },
                };
                notes.Add($"Ricerche importate da {legacySearchesPath}.");
            }

            Save(settingsPath, settings);
            return new SettingsLoadResult(settings, Created: true, notes);
        }

        var json = File.ReadAllText(settingsPath);
        JobbbySettings loaded;
        try
        {
            loaded = JsonSerializer.Deserialize<JobbbySettings>(json, ReadOptions)
                ?? throw new SettingsFileException($"{settingsPath} è vuoto.");
        }
        catch (JsonException ex)
        {
            throw new SettingsFileException($"{settingsPath} non valido al campo '{ex.Path ?? "?"}': {ex.Message}", ex);
        }

        using var document = JsonDocument.Parse(json);
        var unknown = new List<string>();
        CollectUnknownFields(document.RootElement, typeof(JobbbySettings), prefix: "", unknown);
        return new SettingsLoadResult(loaded, Created: false, unknown.Select(field => $"Campo sconosciuto ignorato: {field}").ToList());
    }

    public static void Save(string settingsPath, JobbbySettings settings) =>
        AtomicFile.WriteAllText(settingsPath, JsonSerializer.Serialize(settings, WriteOptions));

    private static void CollectUnknownFields(JsonElement element, Type type, string prefix, List<string> unknown)
    {
        if (element.ValueKind != JsonValueKind.Object || !IsSettingsRecord(type))
            return;

        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetCustomAttribute<JsonPropertyNameAttribute>() is not null)
            .ToDictionary(property => property.GetCustomAttribute<JsonPropertyNameAttribute>()!.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var member in element.EnumerateObject())
        {
            var path = prefix.Length == 0 ? member.Name : $"{prefix}.{member.Name}";
            if (properties.TryGetValue(member.Name, out var property))
                CollectUnknownFields(member.Value, property.PropertyType, path, unknown);
            else
                unknown.Add(path);
        }
    }

    // Only our own settings records are walked: dictionaries (e.g. keywords per language) have free-form keys.
    private static bool IsSettingsRecord(Type type) => type.Namespace == typeof(JobbbySettings).Namespace && type.IsClass && type != typeof(string);
}
