using GraphEngine;
using Xunit;

namespace CvExtraction.Tests;

public class CvLoaderTests
{
    // json/yaml branches never touch the LLM; this would fail the test loudly (wrong
    // JSON shape) if LoadAsync ever called it for those branches by mistake.
    private static readonly ILlmClient UnusedLlmClient = new MockLlmClient("not-called");

    [Fact]
    public async Task LoadAsync_Json_DeserializesDirectlyIntoCvSchema()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cv-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, """
            {
              "name": "Jane Doe",
              "yearsExperience": 7,
              "seniority": "Senior",
              "roles": [
                { "title": "Engineer", "company": "Acme", "stack": ["C#"], "highlights": ["Shipped X"] }
              ],
              "skills": ["C#", "SQL"],
              "languages": ["English"]
            }
            """);

        try
        {
            var cv = await CvLoader.LoadAsync(path, UnusedLlmClient);

            Assert.Equal("Jane Doe", cv.Name);
            Assert.Equal(7, cv.YearsExperience);
            Assert.Equal("Senior", cv.Seniority);
            var role = Assert.Single(cv.Roles);
            Assert.Equal("Engineer", role.Title);
            Assert.Equal("Acme", role.Company);
            Assert.Contains("C#", cv.Skills);
            Assert.Contains("English", cv.Languages);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadAsync_Yaml_DeserializesDirectlyIntoCvSchema()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cv-{Guid.NewGuid():N}.yaml");
        await File.WriteAllTextAsync(path, """
            name: Jane Doe
            yearsExperience: 7
            seniority: Senior
            roles:
              - title: Engineer
                company: Acme
                stack: [C#]
                highlights: [Shipped X]
            skills: [C#, SQL]
            languages: [English]
            """);

        try
        {
            var cv = await CvLoader.LoadAsync(path, UnusedLlmClient);

            Assert.Equal("Jane Doe", cv.Name);
            Assert.Equal(7, cv.YearsExperience);
            var role = Assert.Single(cv.Roles);
            Assert.Equal("Acme", role.Company);
            Assert.Contains("SQL", cv.Skills);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadAsync_Pdf_DelegatesToExistingPipeline_ViaLlmClient()
    {
        const string fixedJson = """
            {"name":"John Smith","yearsExperience":8,"seniority":"Senior","roles":[],"skills":["C#"],"languages":["English"]}
            """;

        var llmClient = new MockLlmClient(fixedJson);
        var pdfPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample-cv.pdf");

        var cv = await CvLoader.LoadAsync(pdfPath, llmClient);

        Assert.Equal("John Smith", cv.Name);
        Assert.Equal(8, cv.YearsExperience);
        Assert.Contains("C#", cv.Skills);
    }

    [Fact]
    public async Task LoadAsync_UnsupportedExtension_Throws()
    {
        await Assert.ThrowsAsync<NotSupportedException>(() => CvLoader.LoadAsync("resume.docx", UnusedLlmClient));
    }
}
