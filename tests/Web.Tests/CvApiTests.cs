using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GraphEngine;
using Microsoft.AspNetCore.Hosting;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using Xunit;

namespace Web.Tests;

public class CvApiTests : IDisposable
{
    private const string ExtractedJson = """{"name":"Anna Rossi","yearsExperience":6,"skills":["Triage"],"location":"Lyon"}""";

    private readonly JobbbyWebFactory _factory = new();

    [Fact]
    public async Task Get_ReturnsTheStructuredCv_AndWhatRunsUse()
    {
        var cv = await _factory.CreateClient().GetFromJsonAsync<JsonElement>("/api/cv");

        Assert.Equal("cv.json", cv.GetProperty("source").GetString());
        Assert.Equal("Candidata", cv.GetProperty("cv").GetProperty("name").GetString());
        Assert.Equal("cv.json", cv.GetProperty("usedByRuns").GetString());
        Assert.False(cv.GetProperty("configuredPath").GetBoolean());
        Assert.False(cv.GetProperty("needsExtraction").GetBoolean());
    }

    [Fact]
    public async Task Get_WithJobbbyCvPath_SaysTheFormDoesNotAffectRuns()
    {
        using var configured = _factory.WithWebHostBuilder(builder => builder.UseSetting("Jobbby:CvPath", Path.Combine(_factory.Root, "altro.json")));

        var cv = await configured.CreateClient().GetFromJsonAsync<JsonElement>("/api/cv");

        Assert.True(cv.GetProperty("configuredPath").GetBoolean());
    }

    [Fact]
    public async Task Upload_Pdf_IsExtracted()
    {
        _factory.Llm = new FuncLlm(_ => ExtractedJson);

        var response = await _factory.CreateAppClient().PostAsync("/api/cv", Upload(Pdf("Anna Rossi"), "qualsiasi.bin"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("cv.pdf", body.GetProperty("source").GetString());
        Assert.Equal("Lyon", body.GetProperty("cv").GetProperty("location").GetString());
        Assert.True(File.Exists(_factory.DataDir.CvExtractedPath));
        Assert.False(File.Exists(_factory.DataDir.CvJsonPath));
    }

    [Fact]
    public async Task Upload_JsonNamedLikeAPdf_IsTreatedAsJson()
    {
        var response = await _factory.CreateAppClient().PostAsync("/api/cv", Upload(Encoding.UTF8.GetBytes("""{"name":"Nuova","acceptsRemote":true}"""), "cv.pdf"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Nuova", body.GetProperty("cv").GetProperty("name").GetString());
        Assert.Contains("acceptsRemote", body.GetProperty("notes").ToString());
    }

    [Fact]
    public async Task Upload_Json_WorksEvenWhenTheLlmIsMisconfigured()
    {
        _factory.LlmConfigurationError = "Missing Llm:Endpoint configuration.";

        var response = await _factory.CreateAppClient().PostAsync("/api/cv", Upload(Encoding.UTF8.GetBytes("""{"name":"Senza LLM"}"""), "cv.json"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Upload_Pdf_WithAMisconfiguredLlm_Is400_WithTheReason()
    {
        _factory.LlmConfigurationError = "Missing Llm:Endpoint configuration.";

        var response = await _factory.CreateAppClient().PostAsync("/api/cv", Upload(Pdf("Anna"), "cv.pdf"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Llm:Endpoint", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Upload_WrongFormat_Is400_WithAMessage()
    {
        var response = await _factory.CreateAppClient().PostAsync("/api/cv", Upload(Encoding.UTF8.GetBytes("ciao"), "cv.txt"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(string.IsNullOrEmpty((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString()));
    }

    [Fact]
    public async Task Upload_WithoutAFile_Is400()
    {
        var response = await _factory.CreateAppClient().PostAsync("/api/cv", new MultipartFormDataContent());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Upload_TooLarge_Is413()
    {
        var response = await _factory.CreateAppClient().PostAsync("/api/cv", Upload(new byte[Host.CvStore.MaxUploadBytes + 1], "cv.pdf"));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task Upload_FailedExtraction_Is502_AndKeepsTheOldCv()
    {
        _factory.Llm = new FuncLlm(_ => throw new LlmException("claude non risponde"));
        var before = File.ReadAllText(_factory.DataDir.CvJsonPath);

        var response = await _factory.CreateAppClient().PostAsync("/api/cv", Upload(Pdf("Anna"), "cv.pdf"));

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Contains("claude non risponde", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
        Assert.Equal(before, File.ReadAllText(_factory.DataDir.CvJsonPath));
        Assert.False(File.Exists(_factory.DataDir.CvPdfPath));
    }

    [Fact]
    public async Task Upload_WithoutTheCustomHeader_Is403()
    {
        var response = await _factory.CreateClient().PostAsync("/api/cv", Upload(Encoding.UTF8.GetBytes("{}"), "cv.json"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Upload_WhileAnotherIsExtracting_Is409()
    {
        var release = new TaskCompletionSource();
        var started = new TaskCompletionSource();
        _factory.Llm = new AsyncFuncLlm(async _ =>
        {
            started.TrySetResult();
            await release.Task;
            return ExtractedJson;
        });
        var client = _factory.CreateAppClient();

        var first = client.PostAsync("/api/cv", Upload(Pdf("Anna"), "cv.pdf"));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var second = await client.PostAsync("/api/cv", Upload(Encoding.UTF8.GetBytes("""{"name":"Altra"}"""), "cv.json"));
        release.SetResult();

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await first).StatusCode);
    }

    [Fact]
    public async Task Put_SavesTheEditedCv()
    {
        var response = await _factory.CreateAppClient().PutAsJsonAsync("/api/cv", new { name = "Anna", location = "Lyon", yearsExperience = 4, skills = new[] { "Triage", "" }, roles = new[] { new { title = "Infermiera", company = "Ospedale" } } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var saved = JsonDocument.Parse(File.ReadAllText(_factory.DataDir.CvExtractedPath)).RootElement;
        Assert.Equal("Lyon", saved.GetProperty("location").GetString());
        Assert.Equal(1, saved.GetProperty("skills").GetArrayLength());
    }

    [Theory]
    [InlineData("""{"yearsExperience":-1}""", "yearsExperience")]
    [InlineData("""{"yearsExperience":"tanti"}""", "yearsExperience")]
    public async Task Put_Invalid_Is400ByField_AndWritesNothing(string body, string field)
    {
        var response = await _factory.CreateAppClient().PutAsync("/api/cv", new StringContent(body, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(field, await response.Content.ReadAsStringAsync());
        Assert.False(File.Exists(_factory.DataDir.CvExtractedPath));
    }

    [Fact]
    public async Task Extract_UsesTheStoredPdf_AndWithoutOneIs400()
    {
        var client = _factory.CreateAppClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/cv/extract", null)).StatusCode);

        File.WriteAllBytes(_factory.DataDir.CvPdfPath, Pdf("Anna"));
        File.Delete(_factory.DataDir.CvJsonPath);
        _factory.Llm = new FuncLlm(_ => ExtractedJson);
        Assert.True((await client.GetFromJsonAsync<JsonElement>("/api/cv")).GetProperty("needsExtraction").GetBoolean());

        var response = await client.PostAsync("/api/cv/extract", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Anna Rossi", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("cv").GetProperty("name").GetString());
    }

    [Fact]
    public async Task DerivedQueries_Disabled_DoesNotCallTheLlm()
    {
        _factory.WriteSettings(JobbbyWebFactory.RunnableSettings());
        _factory.Llm = new FuncLlm(_ => throw new InvalidOperationException("nessuna chiamata attesa"));

        var body = await (await _factory.CreateAppClient().PostAsync("/api/cv/derived-queries", null)).Content.ReadFromJsonAsync<JsonElement>();

        Assert.False(body.GetProperty("enabled").GetBoolean());
        Assert.Equal("nurse", body.GetProperty("configured")[0].GetString());
        Assert.Equal(0, body.GetProperty("derived").GetArrayLength());
    }

    [Fact]
    public async Task DerivedQueries_Enabled_AsksTheLlmWithTheCurrentCv()
    {
        var settings = JobbbyWebFactory.RunnableSettings();
        _factory.WriteSettings(settings with { Searches = settings.Searches with { DeriveFromCv = true } });
        _factory.Llm = new FuncLlm(prompt =>
        {
            Assert.Contains("Triage", prompt);
            return """{"queries":["Infermiera di triage"]}""";
        });

        var body = await (await _factory.CreateAppClient().PostAsync("/api/cv/derived-queries", null)).Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(body.GetProperty("enabled").GetBoolean());
        Assert.Equal("Infermiera di triage", body.GetProperty("derived")[0].GetString());
    }

    [Fact]
    public async Task DerivedQueries_WithoutACv_Is400()
    {
        File.Delete(_factory.DataDir.CvJsonPath);

        var response = await _factory.CreateAppClient().PostAsync("/api/cv/derived-queries", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    public void Dispose() => _factory.Dispose();

    private static MultipartFormDataContent Upload(byte[] content, string fileName)
    {
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return new MultipartFormDataContent { { file, "file", fileName } };
    }

    internal static byte[] Pdf(string text)
    {
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(PageSize.A4);
        page.AddText(text, 12, new PdfPoint(40, 700), builder.AddStandard14Font(Standard14Font.Helvetica));
        return builder.Build();
    }
}
