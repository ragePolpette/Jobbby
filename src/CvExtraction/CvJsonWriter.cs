namespace CvExtraction;

/// <summary>
/// Writes the extracted CV JSON to disk as-is. There is deliberately no schema
/// validation here: the file is meant to be opened and corrected by hand before it's
/// used anywhere else.
/// </summary>
public static class CvJsonWriter
{
    public static Task WriteAsync(string json, string outputPath, CancellationToken cancellationToken = default) =>
        File.WriteAllTextAsync(outputPath, json, cancellationToken);
}
