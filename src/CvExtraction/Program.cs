using CvExtraction;
using GraphEngine;

if (args.Length < 1)
{
    Console.Error.WriteLine("Usage: CvExtraction <path-to-cv.pdf> [output-json-path]");
    return 1;
}

var pdfPath = args[0];
var outputPath = args.Length > 1 ? args[1] : "cv.json";

if (!File.Exists(pdfPath))
{
    Console.Error.WriteLine($"File not found: {pdfPath}");
    return 1;
}

var rawText = PdfTextExtractor.ExtractText(pdfPath);

// MockLlmClient stands in for a real provider until one is wired in; it lets the
// PDF -> prompt -> JSON -> file pipeline be validated end-to-end today.
ILlmClient llmClient = new MockLlmClient("""
    {
      "name": "",
      "yearsExperience": 0,
      "seniority": "",
      "roles": [],
      "skills": [],
      "languages": []
    }
    """);

var extractor = new CvExtractor(llmClient);
var json = await extractor.ExtractCvJsonAsync(rawText);

await CvJsonWriter.WriteAsync(json, outputPath);

Console.WriteLine($"Wrote CV JSON to {outputPath}");
Console.WriteLine("Review and correct it by hand before using it elsewhere - nothing here validates the schema.");

return 0;
