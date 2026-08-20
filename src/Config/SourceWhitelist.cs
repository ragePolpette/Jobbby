using System.Text.Json;
using System.Text.Json.Serialization;

namespace Config;

/// <summary>
/// One allowed external source. <see cref="AuthSecretKey"/> is only the *name* of a
/// secret to resolve at runtime - via `dotnet user-secrets` in development or an
/// environment variable in production, see <see cref="SourceWhitelist.ResolveSecret"/> -
/// never the credential itself. Real credentials never belong in sources.json.
/// </summary>
public sealed record SourceDefinition(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("baseUrl")] string BaseUrl,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("requiresAuth")] bool RequiresAuth,
    [property: JsonPropertyName("authSecretKey")] string? AuthSecretKey);

/// <summary>Loads the whitelist of external sources domain nodes are allowed to query.</summary>
public static class SourceWhitelist
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static IReadOnlyList<SourceDefinition> LoadFromFile(string path) =>
        Load(File.ReadAllText(path));

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
