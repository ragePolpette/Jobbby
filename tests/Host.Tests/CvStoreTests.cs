using System.Text;
using System.Text.Json;
using CvExtraction;
using GraphEngine;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace Host.Tests;

public class CvStoreTests : IDisposable
{
    private const string ExtractedJson = """{"name":"Anna Rossi","yearsExperience":6,"seniority":"senior","skills":["Triage","Triage "," "],"languages":["Italiano"],"location":" Lyon "}""";

    private readonly TempDir _dir = new();
    private readonly DataDir _dataDir;
    private readonly CvStore _store;

    public CvStoreTests()
    {
        _dataDir = new DataDir(_dir.Root);
        _store = new CvStore(_dataDir);
    }

    [Fact]
    public void Read_OnlyPdf_NeedsExtraction()
    {
        File.WriteAllBytes(_dataDir.CvPdfPath, Pdf("Anna Rossi"));

        var state = _store.Read();

        Assert.Equal("cv.pdf", state.Source);
        Assert.Null(state.Cv);
        Assert.True(state.NeedsExtraction);
    }

    [Fact]
    public void Read_JsonOnly_ReadsIt_AndExtractedWins()
    {
        File.WriteAllText(_dataDir.CvJsonPath, """{"name":"Da json"}""");
        Assert.Equal("Da json", _store.Read().Cv!.Name);
        Assert.False(_store.Read().NeedsExtraction);

        File.WriteAllText(_dataDir.CvExtractedPath, """{"name":"Estratto"}""");
        Assert.Equal("Estratto", _store.Read().Cv!.Name);
    }

    [Fact]
    public void Read_UnreadableExtracted_GivesANote_NotAnException()
    {
        File.WriteAllText(_dataDir.CvExtractedPath, """{"name": """);

        var state = _store.Read();

        Assert.Null(state.Cv);
        Assert.Contains(state.Notes, note => note.Contains("cv.extracted.json"));
    }

    [Fact]
    public async Task Upload_Pdf_ExtractsAndWritesPdfAndExtracted_RemovesJson()
    {
        File.WriteAllText(_dataDir.CvJsonPath, """{"name":"Vecchio"}""");
        var pdf = Pdf("Anna Rossi infermiera");
        var llm = new FuncLlm(prompt =>
        {
            Assert.Contains("Anna Rossi infermiera", prompt);
            return ExtractedJson;
        });

        var state = await _store.UploadAsync(pdf, llm);

        Assert.Equal(pdf, File.ReadAllBytes(_dataDir.CvPdfPath));
        Assert.False(File.Exists(_dataDir.CvJsonPath));
        Assert.Equal("cv.pdf", state.Source);
        Assert.Equal("Anna Rossi", state.Cv!.Name);
        Assert.Equal(new[] { "Triage" }, state.Cv.Skills);
        Assert.Equal("Lyon", state.Cv.Location);
        var saved = JsonSerializer.Deserialize<CvData>(File.ReadAllText(_dataDir.CvExtractedPath))!;
        Assert.Equal(new[] { "Triage" }, saved.Skills);
    }

    [Fact]
    public async Task Upload_Json_WritesJsonAndExtracted_RemovesPdf_AndNotesPreferenceFields()
    {
        File.WriteAllBytes(_dataDir.CvPdfPath, Pdf("vecchio"));
        File.WriteAllText(_dataDir.CvExtractedPath, """{"name":"Dal vecchio PDF"}""");
        var json = Encoding.UTF8.GetBytes("""{"name":"Nuovo","minimumSalary":30000,"skills":["A"]}""");

        var state = await _store.UploadAsync(json, new FuncLlm(_ => throw new InvalidOperationException("nessuna chiamata LLM per un JSON")));

        Assert.False(File.Exists(_dataDir.CvPdfPath));
        Assert.Equal(json, File.ReadAllBytes(_dataDir.CvJsonPath));
        Assert.Equal("Nuovo", state.Cv!.Name);
        Assert.Equal("Nuovo", _store.Read().Cv!.Name);
        Assert.Contains(state.Notes, note => note.Contains("minimumSalary"));
    }

    [Fact]
    public async Task Upload_JsonWithBom_IsAccepted()
    {
        var json = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("""{"name":"Bom"}""")).ToArray();

        var state = await _store.UploadAsync(json, new FuncLlm(_ => ""));

        Assert.Equal("Bom", state.Cv!.Name);
    }

    [Theory]
    [InlineData("[1,2]")]
    [InlineData("non è json")]
    [InlineData("{\"name\": ")]
    [InlineData("{\"yearsExperience\": -3}")]
    [InlineData("")]
    public async Task Upload_NotAPdfNorAValidCvObject_IsRejected_NothingWritten(string content)
    {
        await Assert.ThrowsAsync<CvFileException>(() => _store.UploadAsync(Encoding.UTF8.GetBytes(content), new FuncLlm(_ => ExtractedJson)));

        Assert.Empty(Directory.GetFiles(_dir.Root));
    }

    [Fact]
    public async Task Upload_TooLarge_IsRejected()
    {
        var content = new byte[CvStore.MaxUploadBytes + 1];

        await Assert.ThrowsAsync<CvFileException>(() => _store.UploadAsync(content, new FuncLlm(_ => ExtractedJson)));
    }

    [Fact]
    public async Task Upload_PdfWithoutText_IsRejected_WithoutCallingTheLlm()
    {
        var calls = 0;

        var error = await Assert.ThrowsAsync<CvFileException>(() => _store.UploadAsync(Pdf(null), new FuncLlm(_ => { calls++; return ExtractedJson; })));

        Assert.Equal(0, calls);
        Assert.Contains("testo", error.Message);
    }

    [Fact]
    public async Task Upload_BrokenPdf_IsAFileError()
    {
        var content = Encoding.ASCII.GetBytes("%PDF-1.7\nquesto non è un pdf");

        await Assert.ThrowsAsync<CvFileException>(() => _store.UploadAsync(content, new FuncLlm(_ => ExtractedJson)));
    }

    [Theory]
    [InlineData("non json")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("THROW")]
    public async Task Upload_FailedExtraction_LeavesEveryFileUnchanged(string response)
    {
        File.WriteAllBytes(_dataDir.CvPdfPath, Pdf("vecchio"));
        File.WriteAllText(_dataDir.CvExtractedPath, """{"name":"Vecchio"}""");
        File.WriteAllText(_dataDir.CvJsonPath, """{"name":"Json vecchio"}""");
        var before = Snapshot();

        await Assert.ThrowsAsync<CvExtractionException>(() => _store.UploadAsync(Pdf("nuovo"),
            new FuncLlm(_ => response == "THROW" ? throw new LlmException("claude non risponde") : response)));

        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public async Task ExtractPdf_UsesTheStoredPdf()
    {
        File.WriteAllBytes(_dataDir.CvPdfPath, Pdf("Anna Rossi"));

        var state = await _store.ExtractPdfAsync(new FuncLlm(_ => ExtractedJson));

        Assert.False(state.NeedsExtraction);
        Assert.Equal("Anna Rossi", state.Cv!.Name);
        Assert.True(File.Exists(_dataDir.CvExtractedPath));
    }

    [Fact]
    public async Task ExtractPdf_WithoutPdf_IsAFileError()
    {
        await Assert.ThrowsAsync<CvFileException>(() => _store.ExtractPdfAsync(new FuncLlm(_ => ExtractedJson)));
    }

    [Fact]
    public void Save_WritesTheNormalizedCv()
    {
        var state = _store.Save(new CvData { Name = " Anna ", Skills = new() { "B", "b", "" }, Location = "  " });

        Assert.Equal("Anna", state.Cv!.Name);
        Assert.Equal(new[] { "B" }, state.Cv.Skills);
        Assert.Null(state.Cv.Location);
        Assert.Equal("Anna", _store.Read().Cv!.Name);
    }

    [Fact]
    public void Validator_NormalizesRolesAndLists()
    {
        var cv = CvValidator.Normalize(new CvData
        {
            Roles = new()
            {
                new CvRole { Title = " Infermiera ", Company = "", Skills = new() { "Triage", " triage" }, Highlights = new() { " ", "Turni" } },
                new CvRole { Title = " ", Company = " " },
            },
            Languages = new() { "Italiano", "italiano", "Francese" },
        });

        var role = Assert.Single(cv.Roles);
        Assert.Equal("Infermiera", role.Title);
        Assert.Equal(new[] { "Triage" }, role.Skills);
        Assert.Equal(new[] { "Turni" }, role.Highlights);
        Assert.Equal(new[] { "Italiano", "Francese" }, cv.Languages);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(81)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Validator_RejectsImplausibleYears(double years)
    {
        var error = Assert.Single(CvValidator.Validate(new CvData { YearsExperience = years }));

        Assert.Equal("yearsExperience", error.Field);
    }

    [Fact]
    public void Validator_AcceptsAnEmptyCv() => Assert.Empty(CvValidator.Validate(new CvData()));

    public void Dispose() => _dir.Dispose();

    private Dictionary<string, string> Snapshot() =>
        Directory.GetFiles(_dir.Root).ToDictionary(path => Path.GetFileName(path), path => Convert.ToBase64String(File.ReadAllBytes(path)));

    /// <summary>A one-page PDF with the given text, or with no text at all (like a scan).</summary>
    internal static byte[] Pdf(string? text)
    {
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(PageSize.A4);
        if (text is not null)
        {
            var font = builder.AddStandard14Font(Standard14Font.Helvetica);
            page.AddText(text, 12, new PdfPoint(40, 700), font);
        }

        return builder.Build();
    }

    private sealed class FuncLlm(Func<string, string> respond) : ILlmClient
    {
        public Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken = default) => Task.FromResult(respond(prompt));
    }
}
