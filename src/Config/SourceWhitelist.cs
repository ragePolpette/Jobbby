using System.Text.Json;
using System.Text.Json.Serialization;

namespace Config;

/// <summary>
/// One allowed external source. <see cref="AuthSecretKey"/> is only the *name* of a
/// secret to resolve at runtime - via `dotnet user-secrets` in development or an
/// environment variable in production, see <see cref="SourceWhitelist.ResolveSecret"/> -
/// never the credential itself. Real credentials never belong in sources.json.
/// </summary>
// Property-based (not positional) so it has a parameterless constructor and settable
// properties: YamlDotNet's default object factory needs both, System.Text.Json is fine
// with either style.
public sealed record SourceDefinition
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("baseUrl")]
    public string BaseUrl { get; init; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("requiresAuth")]
    public bool RequiresAuth { get; init; }

    [JsonPropertyName("authSecretKey")]
    public string? AuthSecretKey { get; init; }
}

/// <summary>Loads the whitelist of external sources domain nodes are allowed to query.</summary>
public static class SourceWhitelist
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Loads the whitelist from a .json, .yaml or .yml file, chosen by extension.</summary>
    public static IReadOnlyList<SourceDefinition> LoadFromFile(string path) =>
        ConfigLoader.Load<List<SourceDefinition>>(path);

    public static IReadOnlyList<SourceDefinition> Load(string json) =>
        JsonSerializer.Deserialize<List<SourceDefinition>>(json, SerializerOptions)
        ?? new List<SourceDefinition>();

    /// <summary>
    /// Resolves the actual credential for a source's <see cref="SourceDefinition.AuthSecretKey"/>.
    /// In development, set it with `dotnet user-secrets set &lt;key&gt; &lt;value&gt;` in the
    /// consuming project (user-secrets surface as environment-like configuration); in
    /// production, set an environment variable of the same name.
    /// </summary>
    public static string? ResolveSecret(string secretKey) => Environment.GetEnvironmentVariable(secretKey);
}
