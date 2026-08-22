using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;

namespace Config;

/// <summary>
/// One allowed external source. <see cref="AuthSecretKey"/> is only the *name* of a
/// secret to resolve at runtime via <see cref="SourceWhitelist.ResolveSecret"/> - never
/// the credential itself. Real credentials never belong in sources.json.
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
    /// The configuration secrets are actually resolved from - set once at startup by the
    /// host application (see Host/Program.cs), which builds it with
    /// <c>.AddEnvironmentVariables().AddUserSecrets&lt;Program&gt;()</c>. Left null, e.g.
    /// in tests that call <see cref="ResolveSecret"/> directly, resolution falls back to
    /// <see cref="Environment.GetEnvironmentVariable"/> below.
    /// </summary>
    public static IConfiguration? Configuration { get; set; }

    /// <summary>
    /// Resolves the actual credential for a source's <see cref="SourceDefinition.AuthSecretKey"/>
    /// (or any other secret key) from <see cref="Configuration"/> - which is where
    /// `dotnet user-secrets set &lt;key&gt; &lt;value&gt;` values actually surface in
    /// development - falling back to a raw environment variable of the same name, which
    /// is what production actually sets.
    /// </summary>
    public static string? ResolveSecret(string secretKey) =>
        Configuration?[secretKey] ?? Environment.GetEnvironmentVariable(secretKey);
}
