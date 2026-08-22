using System.Text.Json;
using Config;
using GraphEngine;

namespace CvExtraction;

/// <summary>
/// Loads a <see cref="CvData"/> from whatever format it's in: .json/.yaml/.yml deserialize
/// straight into the schema, no LLM involved; .pdf delegates to the existing
/// PdfPig + ILlmClient extraction pipeline. Callers get the same typed result either way.
/// </summary>
public static class CvLoader
{
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

    private static async Task<CvData> LoadFromPdfAsync(string path, ILlmClient llmClient, CancellationToken cancellationToken)
    {
        var rawText = PdfTextExtractor.ExtractText(path);
        var extractor = new CvExtractor(llmClient);
        var json = await extractor.ExtractCvJsonAsync(rawText, cancellationToken).ConfigureAwait(false);

        return JsonSerializer.Deserialize<CvData>(json, JsonOptions)
            ?? throw new InvalidOperationException($"LLM response for '{path}' could not be parsed as CV JSON.");
    }
}
