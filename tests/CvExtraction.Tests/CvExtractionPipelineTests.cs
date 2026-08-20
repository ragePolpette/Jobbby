using System.Text.Json;
using CvExtraction;
using GraphEngine;
using Xunit;

namespace CvExtraction.Tests;

public class CvExtractionPipelineTests
{
    private const string FakeRawText = """
        John Smith
        Senior Software Engineer - 8 years of experience

        Acme Corp - Senior Software Engineer
        - Led migration to microservices
        Stack: C#, .NET, Docker
        """;

    private const string FixedCvJson = """
        {"name":"John Smith","yearsExperience":8,"seniority":"Senior","roles":[{"title":"Senior Software Engineer","company":"Acme Corp","stack":["C#",".NET","Docker"],"highlights":["Led migration to microservices"]}],"skills":["C#",".NET","Docker"],"languages":["English"]}
        """;

    [Fact]
    public async Task ExtractCvJsonAsync_ReturnsTheLlmResponseVerbatim()
    {
        var llmClient = new MockLlmClient(FixedCvJson);
        var extractor = new CvExtractor(llmClient);

        var result = await extractor.ExtractCvJsonAsync(FakeRawText);

        Assert.Equal(FixedCvJson, result);
    }

    [Fact]
    public async Task Pipeline_GivenFakeTextAndMockLlm_WritesJsonFileMatchingSchema()
    {
        var llmClient = new MockLlmClient(FixedCvJson);
        var extractor = new CvExtractor(llmClient);
        var outputPath = Path.Combine(Path.GetTempPath(), $"cv-{Guid.NewGuid():N}.json");

        try
        {
            var json = await extractor.ExtractCvJsonAsync(FakeRawText);
            await CvJsonWriter.WriteAsync(json, outputPath);

            Assert.True(File.Exists(outputPath));

            var written = await File.ReadAllTextAsync(outputPath);
            Assert.Equal(FixedCvJson, written);

            using var document = JsonDocument.Parse(written);
            var root = document.RootElement;

            Assert.Equal("John Smith", root.GetProperty("name").GetString());
            Assert.Equal(8, root.GetProperty("yearsExperience").GetInt32());
            Assert.Equal("Senior", root.GetProperty("seniority").GetString());
            Assert.Equal(1, root.GetProperty("roles").GetArrayLength());
            Assert.Contains("C#", root.GetProperty("skills").EnumerateArray().Select(e => e.GetString()));
            Assert.Contains("English", root.GetProperty("languages").EnumerateArray().Select(e => e.GetString()));
        }
        finally
        {
            File.Delete(outputPath);
        }
    }
}
