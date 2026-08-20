using System.Text;
using UglyToad.PdfPig;

namespace CvExtraction;

/// <summary>Thin wrapper around PdfPig: reads every page of a PDF and concatenates its
/// raw text. No cleanup or structuring - that's the LLM's job downstream.</summary>
public static class PdfTextExtractor
{
    public static string ExtractText(string pdfPath)
    {
        using var document = PdfDocument.Open(pdfPath);

        var text = new StringBuilder();
        foreach (var page in document.GetPages())
            text.AppendLine(page.Text);

        return text.ToString();
    }
}
