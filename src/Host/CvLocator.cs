using Microsoft.Extensions.Configuration;

namespace Host;

/// <summary>Where the CV of an installation is: <c>Jobbby:CvPath</c> if set, otherwise the DataDir's CV files.</summary>
public static class CvLocator
{
    /// <summary>The structured CV (edited in the UI) wins over the uploaded JSON, which wins over the PDF.</summary>
    public static string? Find(DataDir dataDir, IConfiguration configuration) =>
        configuration["Jobbby:CvPath"]
        ?? new[] { dataDir.CvExtractedPath, dataDir.CvJsonPath, dataDir.CvPdfPath }.FirstOrDefault(File.Exists);

    public static string NotFoundMessage(DataDir dataDir) =>
        $"Nessun CV trovato: metti cv.pdf o cv.json in {dataDir.Root} (oppure imposta Jobbby:CvPath).";
}
