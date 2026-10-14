using System.Text.Json;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Config;

/// <summary>
/// Generic config file loader, not tied to the source whitelist: deserializes any type
/// from JSON or YAML, chosen by the path's extension (.json, or .yaml/.yml). C# property
/// names are matched case-insensitively for JSON and against their camelCase form for
/// YAML, so a config type needs no format-specific attributes to support both.
/// </summary>
public static class ConfigLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly IDeserializer YamlDeserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static T Load<T>(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        var content = File.ReadAllText(path);

        return extension switch
        {
            ".json" => JsonSerializer.Deserialize<T>(content, JsonOptions)
                ?? throw new InvalidOperationException($"'{path}' deserialized to null."),
            ".yaml" or ".yml" => YamlDeserializer.Deserialize<T>(content)
                ?? throw new InvalidOperationException($"'{path}' deserialized to null."),
            _ => throw new NotSupportedException(
                $"Unsupported config file extension '{extension}' for '{path}'. Expected .json, .yaml or .yml."),
        };
    }
}
