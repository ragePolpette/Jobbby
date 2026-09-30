using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ApplicationLedger;
using GraphEngine;
using Host;
using Xunit;

namespace Web.Tests;

public class PresentationApiTests : IDisposable
{
    private const string RunId = "20260930-120000-dry-abcdef12";
    private const string Message = """{"matches":["Triage"],"body":"Ho letto il vostro annuncio."}""";

    private readonly JobbbyWebFactory _factory = new();
    private readonly ApplicationRecord _pending;

    public PresentationApiTests()
    {
        _factory.WriteSettings(JobbbyWebFactory.RunnableSettings());
        var key = PostingIdentity.Key("Clinica Sole", "Infermiera");
        _pending = new ApplicationRecord(key, "Clinica Sole", "Infermiera", "https://x/1", DateTimeOffset.UtcNow.AddMinutes(-10), ApplicationOutcomes.Pending, PostingIdentity.Id(key), "Da decidere")
        {
            ApplyUrl = "https://x/1",
            Excerpt = "Clinica Sole cerca infermiera.",
            Confidence = 0.4,
            Category = "Borderline",
        };
        new ApplicationLedger.ApplicationLedger(_factory.DataDir.ApplicationsPath).RecordOutcome(_pending);
        new RunStore(_factory.DataDir).Save(new RunRecord
        {
            RunId = RunId,
            Mode = "dry",
            Status = RunStatus.Completed,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            Postings = new()
            {
                new RunPosting("dry1", "Aiuto infermiera", "Clinica Luna", "https://x/2", "Adzuna", "Local", PostingStatus.Evaluated, "da decidere",
                    _pending with { Title = "Aiuto infermiera", Company = "Clinica Luna", PostingId = "dry1", RunId = RunId }, null),
            },
        });
        _factory.Llm = Routed(confidence: 0.9);
    }

    private static ILlmClient Routed(double confidence, bool brokenMessage = false) => new FuncLlm(prompt =>
    {
        if (prompt.Contains("Scrivi il messaggio con cui", StringComparison.Ordinal))
            return brokenMessage ? throw new LlmException("claude non risponde") : Message;
        if (prompt.Contains("Estrai le seguenti informazioni", StringComparison.Ordinal))
            return """{"seniorityLevel":"","requiredSkills":["Triage"],"workMode":"onsite"}""";
        return $$"""{"category":"Strong","reasoning":"Triage.","confidence":{{confidence.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}""";
    });

    private ApplicationLedger.ApplicationLedger Ledger() => new(_factory.DataDir.ApplicationsPath);

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    [Fact]
    public async Task Generate_OnALedgerPosting_SavesTheMessage()
    {
        var response = await _factory.CreateAppClient().PostAsync($"/api/postings/{_pending.PostingId}/presentation", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var saved = Ledger().Current(_pending.PostingId!)!;
        Assert.Contains("Ho letto il vostro annuncio.", saved.Presentation!.Text);
        Assert.Equal("Triage", saved.Presentation.Matches.Single());
        Assert.Equal(ApplicationOutcomes.Pending, saved.Outcome);
    }

    [Fact]
    public async Task Edit_SavesTheTextAsEdited()
    {
        var client = _factory.CreateAppClient();
        await client.PostAsync($"/api/postings/{_pending.PostingId}/presentation", null);

        var response = await client.PutAsync($"/api/postings/{_pending.PostingId}/presentation", Json("""{"text":"Testo mio."}"""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var saved = Ledger().Current(_pending.PostingId!)!.Presentation!;
        Assert.Equal("Testo mio.", saved.Text);
        Assert.True(saved.Edited);
        Assert.Equal("Triage", saved.Matches.Single());
    }

    [Fact]
    public async Task Edit_EmptyText_Is400()
    {
        var response = await _factory.CreateAppClient().PutAsync($"/api/postings/{_pending.PostingId}/presentation", Json("""{"text":"  "}"""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Generate_OnADryRunPosting_SavesInTheRunFile()
    {
        var response = await _factory.CreateAppClient().PostAsync($"/api/runs/{RunId}/postings/dry1/presentation", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Ho letto", new RunStore(_factory.DataDir).Load(RunId)!.Postings.Single().Record!.Presentation!.Text);
        Assert.False(File.Exists(_factory.DataDir.ApplicationsPath) && Ledger().Current("dry1") is not null);
    }

    [Fact]
    public async Task FullText_OnALedgerPosting_ReevaluatesAndSaves()
    {
        var response = await _factory.CreateAppClient().PutAsync($"/api/postings/{_pending.PostingId}/full-text", Json("""{"text":"Clinica Sole cerca infermiera con triage, testo completo."}"""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var saved = Ledger().Current(_pending.PostingId!)!;
        Assert.Equal(ApplicationOutcomes.Shortlisted, saved.Outcome);
        Assert.StartsWith("Clinica Sole cerca infermiera con triage", saved.FullText!.Text);
        Assert.Equal(0.4, saved.FullText.ExcerptEvaluation.Confidence);
    }

    [Fact]
    public async Task FullText_OnADryRunPosting_UpdatesTheRunFile()
    {
        var response = await _factory.CreateAppClient().PutAsync($"/api/runs/{RunId}/postings/dry1/full-text", Json("""{"text":"Testo completo della clinica."}"""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var record = new RunStore(_factory.DataDir).Load(RunId)!.Postings.Single().Record!;
        Assert.Equal("Testo completo della clinica.", record.FullText!.Text);
        Assert.Equal(ApplicationOutcomes.Shortlisted, record.Outcome);
        Assert.Equal("dry1", record.PostingId);
    }

    [Fact]
    public async Task FullText_TooLong_Is400_AndWritesNothing()
    {
        var before = File.ReadAllText(_factory.DataDir.ApplicationsPath);
        var body = JsonSerializer.Serialize(new { text = new string('a', UserFullText.MaxLength + 1) });

        var response = await _factory.CreateAppClient().PutAsync($"/api/postings/{_pending.PostingId}/full-text", Json(body));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, File.ReadAllText(_factory.DataDir.ApplicationsPath));
    }

    [Fact]
    public async Task Approve_GeneratesTheMessage()
    {
        var response = await _factory.CreateAppClient().PostAsync($"/api/postings/{_pending.PostingId}/decision", Json("""{"decision":"approve"}"""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var saved = Ledger().Current(_pending.PostingId!)!;
        Assert.Equal(ApplicationOutcomes.Approved, saved.Outcome);
        Assert.Contains("Ho letto", saved.Presentation!.Text);
    }

    [Fact]
    public async Task Approve_WithABrokenLlm_KeepsTheDecision_AndReportsTheMessageError()
    {
        _factory.Llm = Routed(0.9, brokenMessage: true);

        var response = await _factory.CreateAppClient().PostAsync($"/api/postings/{_pending.PostingId}/decision", Json("""{"decision":"approve"}"""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("claude non risponde", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("presentationError").GetString());
        Assert.Equal(ApplicationOutcomes.Approved, Ledger().Current(_pending.PostingId!)!.Outcome);
    }

    [Fact]
    public async Task Approve_WithMessagesDisabled_DoesNotCallTheLlm()
    {
        var settings = JobbbyWebFactory.RunnableSettings();
        _factory.WriteSettings(settings with { Presentation = settings.Presentation with { Enabled = false } });
        _factory.Llm = new FuncLlm(_ => throw new InvalidOperationException("nessuna chiamata attesa"));

        var response = await _factory.CreateAppClient().PostAsync($"/api/postings/{_pending.PostingId}/decision", Json("""{"decision":"approve"}"""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(Ledger().Current(_pending.PostingId!)!.Presentation);
    }

    [Fact]
    public async Task Preview_UsesUnsavedSettings_AndWritesNothing()
    {
        var before = File.ReadAllText(_factory.DataDir.ApplicationsPath);
        var body = JsonSerializer.Serialize(new { presentation = new { enabled = true, opening = "Buongiorno {azienda}.", closing = "Saluti", tone = "cordiale", length = "breve", language = "annuncio", extraInstructions = "" } });

        var response = await _factory.CreateAppClient().PostAsync("/api/presentation/preview", Json(body));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var preview = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.StartsWith("Buongiorno Clinica Sole.", preview.GetProperty("text").GetString());
        Assert.Equal("Infermiera", preview.GetProperty("posting").GetProperty("title").GetString());
        Assert.Equal(before, File.ReadAllText(_factory.DataDir.ApplicationsPath));
    }

    [Fact]
    public async Task Preview_InvalidSettings_Is400ByField()
    {
        var body = JsonSerializer.Serialize(new { presentation = new { enabled = true, opening = "{telefono}", closing = "", tone = "formale", length = "breve", language = "annuncio", extraInstructions = "" } });

        var response = await _factory.CreateAppClient().PostAsync("/api/presentation/preview", Json(body));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("presentation.opening", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task UnknownPostingOrRun_Is404()
    {
        var client = _factory.CreateAppClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/api/postings/0123456789abcdef0123456789abcdef/presentation", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/api/runs/{RunId}/postings/zzz/presentation", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsync("/api/runs/sconosciuta/postings/dry1/full-text", Json("""{"text":"x"}"""))).StatusCode);
    }

    [Fact]
    public async Task MessageGenerationError_Is502_AndWritesNothing()
    {
        _factory.Llm = Routed(0.9, brokenMessage: true);
        var before = File.ReadAllText(_factory.DataDir.ApplicationsPath);

        var response = await _factory.CreateAppClient().PostAsync($"/api/postings/{_pending.PostingId}/presentation", null);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(before, File.ReadAllText(_factory.DataDir.ApplicationsPath));
    }

    [Fact]
    public async Task PdfOnlyCv_WithTheLlmDown_Is502_NotAServerError()
    {
        File.Delete(_factory.DataDir.CvJsonPath);
        File.WriteAllBytes(_factory.DataDir.CvPdfPath, CvApiTests.Pdf("Anna"));
        _factory.Llm = new FuncLlm(_ => throw new LlmException("claude non risponde"));
        var client = _factory.CreateAppClient();

        var generate = await client.PostAsync($"/api/postings/{_pending.PostingId}/presentation", null);
        var approve = await client.PostAsync($"/api/postings/{_pending.PostingId}/decision", Json("""{"decision":"approve"}"""));

        Assert.Equal(HttpStatusCode.BadGateway, generate.StatusCode);
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
        Assert.Contains("claude non risponde", (await approve.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("presentationError").GetString());
    }

    [Fact]
    public async Task ARunStillInProgress_Is409()
    {
        var release = new TaskCompletionSource();
        _factory.Source = new BlockingSource(release.Task);
        var client = _factory.CreateAppClient();
        var started = await (await client.PostAsync("/api/runs", Json("""{"mode":"dry"}"""))).Content.ReadFromJsonAsync<JsonElement>();
        var runId = started.GetProperty("runId").GetString();

        var response = await client.PutAsync($"/api/runs/{runId}/postings/dry1/full-text", Json("""{"text":"x"}"""));
        release.SetResult();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    private sealed class BlockingSource(Task release) : JobPostings.IJobSource
    {
        public async Task<IReadOnlyList<JobPostings.RawPosting>> FetchAsync(Config.SourceDefinition source, JobPostings.JobSearchRequest request, Reporting.SourceCursor? cursor = null, CancellationToken cancellationToken = default)
        {
            await release.WaitAsync(cancellationToken);
            return Array.Empty<JobPostings.RawPosting>();
        }
    }

    public void Dispose() => _factory.Dispose();
}
