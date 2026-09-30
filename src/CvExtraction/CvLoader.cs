using System.Text.Json;
using Config;
using GraphEngine;

namespace CvExtraction;

/// <summary>
/// Loads a <see cref="CvData"/> from whatever format it's in: .json/.yaml/.yml deserialize
/// straight into the schema, no LLM involved; .pdf delegates to the existing
/// PdfPig + ILlmClient extraction pipeline. Callers get the same typed result either way.
/// </summary>
/// <param name="IgnoredFields">Preference fields found in the file that now live in settings.json.</param>
public sealed record CvLoadResult(CvData Cv, IReadOnlyList<string> IgnoredFields);

public static class CvLoader
{
    /// <summary>Preferences that used to live in the CV and are now user settings (area, salary, remote).</summary>
    public static readonly IReadOnlyList<string> PreferenceFields = new[] { "minimumSalary", "desiredLocations", "acceptsRemote" };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static async Task<CvData> LoadAsync(string path, ILlmClient llmClient, CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();

        return extension switch
        {
            ".json" or ".yaml" or ".yml" => ConfigLoader.Load<CvData>(path),
            ".pdf" => await LoadFromPdfAsync(path, llmClient, cancellationToken).ConfigureAwait(false),
            _ => throw new NotSupportedException(
                $"Unsupported CV file extension '{extension}' for '{path}'. Expected .json, .yaml, .yml or .pdf."),
        };
    }

    public static async Task<CvLoadResult> LoadWithNotesAsync(string path, ILlmClient llmClient, CancellationToken cancellationToken = default)
    {
        var cv = await LoadAsync(path, llmClient, cancellationToken).ConfigureAwait(false);
        return new CvLoadResult(cv, FindPreferenceFields(path));
    }

    private static IReadOnlyList<string> FindPreferenceFields(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension == ".json")
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return Array.Empty<string>();
            var names = document.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return PreferenceFields.Where(names.Contains).ToList();
        }

        if (extension is ".yaml" or ".yml")
        {
            var keys = File.ReadAllLines(path)
                .Where(line => line.Length > 0 && !char.IsWhiteSpace(line[0]) && line.Contains(':'))
                .Select(line => line[..line.IndexOf(':')].Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            return PreferenceFields.Where(keys.Contains).ToList();
        }

        return Array.Empty<string>();
    }

    private static async Task<CvData> LoadFromPdfAsync(string path, ILlmClient llmClient, CancellationToken cancellationToken)
    {
        var rawText = PdfTextExtractor.ExtractText(path);
        var extractor = new CvExtractor(llmClient);
        var json = await extractor.ExtractCvJsonAsync(rawText, cancellationToken).ConfigureAwait(false);

        return JsonSerializer.Deserialize<CvData>(json, JsonOptions)
            ?? throw new InvalidOperationException($"LLM response for '{path}' could not be parsed as CV JSON.");
    }
}
