using System.Text;
using System.Text.Json;
using Config;
using CvExtraction;
using GraphEngine;

namespace Host;

/// <param name="Source">The uploaded file the structured CV comes from (<c>cv.json</c> or <c>cv.pdf</c>), if any.</param>
/// <param name="Cv">The structured CV runs use, or null when there is none yet (or it cannot be read).</param>
/// <param name="NeedsExtraction">Only a PDF is there: it has to be extracted before it can be edited.</param>
public sealed record CvState(string? Source, CvData? Cv, DateTimeOffset? UpdatedAt, bool NeedsExtraction, IReadOnlyList<string> Notes);

/// <summary>The uploaded file is not usable (wrong format, too large, unreadable, no text): nothing was written.</summary>
public sealed class CvFileException(string message) : Exception(message);

/// <summary>The LLM could not turn the PDF into a structured CV: nothing was written.</summary>
public sealed class CvExtractionException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// The CV files of a <see cref="DataDir"/>: the uploaded original (<c>cv.pdf</c> or <c>cv.json</c>, one at a
/// time) and the structured CV runs use (<c>cv.extracted.json</c>). Everything is parsed, extracted and
/// validated before the first write, so a failure leaves the previous CV as it was.
/// </summary>
public sealed class CvStore(DataDir dataDir)
{
    public const long MaxUploadBytes = 5 * 1024 * 1024;

    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public CvState Read()
    {
        var notes = new List<string>();
        var source = File.Exists(dataDir.CvJsonPath) ? dataDir.CvJsonPath : File.Exists(dataDir.CvPdfPath) ? dataDir.CvPdfPath : null;
        var hasExtracted = File.Exists(dataDir.CvExtractedPath);
        var structured = hasExtracted ? dataDir.CvExtractedPath : source == dataDir.CvJsonPath ? source : null;

        CvData? cv = null;
        if (structured is not null)
        {
            try
            {
                cv = JsonSerializer.Deserialize<CvData>(File.ReadAllText(structured), ReadOptions);
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                notes.Add($"{Path.GetFileName(structured)} non leggibile ({ex.Message}): ricarica il CV o correggi il file.");
            }
        }

        var updated = structured ?? source;
        return new CvState(
            source is null ? null : Path.GetFileName(source),
            cv,
            updated is null ? null : new DateTimeOffset(File.GetLastWriteTimeUtc(updated), TimeSpan.Zero),
            NeedsExtraction: !hasExtracted && source == dataDir.CvPdfPath,
            notes);
    }

    /// <summary>A PDF (recognised by its content, whatever the file name) or a JSON CV.</summary>
    public Task<CvState> UploadAsync(byte[] content, ILlmClient llm, CancellationToken cancellationToken = default) =>
        UploadAsync(content, () => llm, cancellationToken);

    /// <summary>The LLM is built only for a PDF: a JSON upload works even when the LLM settings are incomplete.</summary>
    public async Task<CvState> UploadAsync(byte[] content, Func<ILlmClient> llm, CancellationToken cancellationToken = default)
    {
        if (content.Length > MaxUploadBytes)
            throw new CvFileException($"Il file supera il limite di {MaxUploadBytes / (1024 * 1024)} MB.");

        if (IsPdf(content))
        {
            var extracted = await ExtractAsync(content, llm(), cancellationToken).ConfigureAwait(false);
            // The structured CV first: if the process stops between the writes, runs already use the new CV.
            WriteStructured(extracted);
            AtomicFile.WriteAllBytes(dataDir.CvPdfPath, content);
            File.Delete(dataDir.CvJsonPath);
            return Read();
        }

        var (cv, notes) = ParseJson(content);
        WriteStructured(cv);
        AtomicFile.WriteAllBytes(dataDir.CvJsonPath, content);
        File.Delete(dataDir.CvPdfPath);
        var state = Read();
        return state with { Notes = state.Notes.Concat(notes).ToList() };
    }

    /// <summary>Extracts the PDF already in the DataDir (e.g. one copied there before the CV page existed).</summary>
    public async Task<CvState> ExtractPdfAsync(ILlmClient llm, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(dataDir.CvPdfPath))
            throw new CvFileException("Nessun cv.pdf da estrarre: carica un CV.");

        var extracted = await ExtractAsync(await File.ReadAllBytesAsync(dataDir.CvPdfPath, cancellationToken).ConfigureAwait(false), llm, cancellationToken).ConfigureAwait(false);
        WriteStructured(extracted);
        return Read();
    }

    /// <summary>Saves the structured CV edited in the UI.</summary>
    public CvState Save(CvData cv)
    {
        var normalized = CvValidator.Normalize(cv);
        var errors = CvValidator.Validate(normalized);
        if (errors.Count > 0)
            throw new SettingsValidationException(errors);
        WriteStructured(normalized);
        return Read();
    }

    private void WriteStructured(CvData cv) =>
        AtomicFile.WriteAllText(dataDir.CvExtractedPath, JsonSerializer.Serialize(cv, WriteOptions));

    private static bool IsPdf(byte[] content) =>
        content.Length >= 5 && content.AsSpan(0, 5).SequenceEqual("%PDF-"u8);

    private static async Task<CvData> ExtractAsync(byte[] pdf, ILlmClient llm, CancellationToken cancellationToken)
    {
        string text;
        try
        {
            text = PdfTextExtractor.ExtractText(pdf);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new CvFileException($"Il PDF non si riesce a leggere: {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(text))
            throw new CvFileException("Il PDF non contiene testo selezionabile (forse è una scansione): carica un PDF con testo o un JSON.");

        string response;
        try
        {
            response = await new CvExtractor(llm).ExtractCvJsonAsync(text, cancellationToken).ConfigureAwait(false);
        }
        catch (LlmException ex)
        {
            throw new CvExtractionException($"Estrazione del CV non riuscita: {ex.Message}", ex);
        }

        CvData? cv;
        try
        {
            cv = JsonSerializer.Deserialize<CvData>(StripFences(response), ReadOptions);
        }
        catch (JsonException ex)
        {
            throw new CvExtractionException($"L'LLM non ha restituito un CV in JSON valido: {ex.Message}", ex);
        }

        if (cv is null)
            throw new CvExtractionException("L'LLM non ha restituito un CV.");

        var normalized = CvValidator.Normalize(cv);
        var errors = CvValidator.Validate(normalized);
        if (errors.Count > 0)
            throw new CvExtractionException("CV estratto non valido: " + string.Join("; ", errors.Select(e => e.Message)));
        return normalized;
    }

    private static (CvData Cv, IReadOnlyList<string> Notes) ParseJson(byte[] content)
    {
        var text = Encoding.UTF8.GetString(content).TrimStart('﻿');
        CvData? cv;
        var notes = new List<string>();
        try
        {
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new CvFileException("Il JSON deve essere un oggetto con i campi del CV.");

            var names = document.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            notes.AddRange(CvLoader.PreferenceFields.Where(names.Contains)
                .Select(field => $"Campo ignorato: {field} (ora si imposta nelle Impostazioni)."));
            cv = document.RootElement.Deserialize<CvData>(ReadOptions);
        }
        catch (JsonException ex)
        {
            throw new CvFileException($"Il file non è né un PDF né un JSON valido: {ex.Message}");
        }

        var normalized = CvValidator.Normalize(cv!);
        var errors = CvValidator.Validate(normalized);
        if (errors.Count > 0)
            throw new CvFileException("CV non valido: " + string.Join("; ", errors.Select(e => e.Message)));
        return (normalized, notes);
    }

    private static string StripFences(string response)
    {
        var text = response.Trim();
        if (!text.StartsWith("```", StringComparison.Ordinal))
            return text;
        var firstLine = text.IndexOf('\n');
        var end = text.LastIndexOf("```", StringComparison.Ordinal);
        return firstLine < 0 || end <= firstLine ? text : text[(firstLine + 1)..end].Trim();
    }
}
