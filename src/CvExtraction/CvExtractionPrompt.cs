namespace CvExtraction;

/// <summary>Builds the prompt asking an LLM to turn raw CV text into the CV JSON schema.</summary>
public static class CvExtractionPrompt
{
    public static string Build(string rawText) => $$"""
        Extract structured data from the raw CV/resume text below.

        Return ONLY valid JSON matching exactly this schema - no markdown fences, no commentary:
        {
          "name": string,
          "yearsExperience": number,
          "seniority": string,
          "roles": [
            { "title": string, "company": string, "stack": [string], "highlights": [string] }
          ],
          "skills": [string],
          "languages": [string]
        }

        Raw CV text:
        ---
        {{rawText}}
        ---
        """;
}
