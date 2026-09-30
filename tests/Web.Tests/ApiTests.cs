using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ApplicationLedger;
using Config;
using Host;
using JobPostings;
using Xunit;

namespace Web.Tests;

public class ApiTests : IDisposable
{
    private readonly JobbbyWebFactory _factory = new();

    // --- settings and secrets

    [Fact]
    public async Task Settings_GetReturnsTheFile_PutValidSaves()
    {
        var client = _factory.CreateAppClient();
        var settings = JobbbyWebFactory.RunnableSettings();

        var put = await client.PutAsync("/api/settings", Json(JsonSerializer.Serialize(settings)));
        var get = await client.GetFromJsonAsync<JsonElement>("/api/settings");

        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        Assert.Equal("de", get.GetProperty("area").GetProperty("country").GetString());
        Assert.Equal("de", SettingsStore.LoadOrCreate(_factory.DataDir.SettingsPath, null).Settings.Area.Country);
    }

    [Theory]
    [InlineData("""{"area":{"country":"xx"}}""", "area.country")]
    [InlineData("""{"area":{"where":null}}""", "area.where")]
    [InlineData("""{"area":{"distanceKm":"trenta"}}""", "area.distanceKm")]
    public async Task Settings_PutInvalid_Returns400ByField_AndKeepsTheFile(string body, string field)
    {
        _factory.WriteSettings(JobbbyWebFactory.RunnableSettings());
        var before = File.ReadAllText(_factory.DataDir.SettingsPath);
        var client = _factory.CreateAppClient();

        var response = await client.PutAsync("/api/settings", Json(body));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        Assert.Contains(errors.EnumerateArray(), e => e.GetProperty("field").GetString()!.Contains(field));
        Assert.Equal(before, File.ReadAllText(_factory.DataDir.SettingsPath));
    }

    [Fact]
    public async Task SecretsStatus_ReportsPresenceOnly()
    {
        var status = await _factory.CreateClient().GetFromJsonAsync<Dictionary<string, JsonElement>>("/api/secrets/status");

        Assert.Contains("Adzuna:AppKey", status!.Keys);
        Assert.All(status.Values, value => Assert.True(value.ValueKind is JsonValueKind.True or JsonValueKind.False));
    }

    // --- runs

    [Fact]
    public async Task Run_Starts_Streams_Completes_AndIsListed()
    {
        _factory.WriteSettings(JobbbyWebFactory.RunnableSettings());
        _factory.Source = new MockJobSource(_ => new[] { Posting("https://x/1") });
        var client = _factory.CreateAppClient();

        var start = await client.PostAsync("/api/runs", Json("""{"mode":"dry"}"""));
        var runId = (await start.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("runId").GetString()!;
        var stream = await client.GetStringAsync("/api/runs/current/events");

        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
        Assert.Contains("data: ", stream);
        Assert.Contains("event: end", stream);
        Assert.Contains("\"completed\"", stream);
        var runs = await client.GetFromJsonAsync<JsonElement>("/api/runs");
        Assert.Contains(runs.EnumerateArray(), run => run.GetProperty("runId").GetString() == runId);
        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/runs/{runId}");
        Assert.Equal("Completed", detail.GetProperty("run").GetProperty("status").GetString());
        Assert.Single(detail.GetProperty("postings").EnumerateArray());
    }

    [Fact]
    public async Task Run_SecondStartWhileRunning_Is409_AndCancelInterrupts()
    {
        _factory.WriteSettings(JobbbyWebFactory.RunnableSettings());
        _factory.Source = new MockJobSource(_ => new[] { Posting("https://x/1") });
        var blocked = new TaskCompletionSource();
        _factory.Llm = new PromptRoutedLlm(async token =>
        {
            blocked.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        });
        var client = _factory.CreateAppClient();

        var first = await client.PostAsync("/api/runs", Json("""{"mode":"normal"}"""));
        await blocked.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var second = await client.PostAsync("/api/runs", Json("""{"mode":"dry"}"""));
        var cancel = await client.PostAsync("/api/runs/current/cancel", Json("{}"));
        await client.GetStringAsync("/api/runs/current/events");
        var current = await client.GetFromJsonAsync<JsonElement>("/api/runs/current");

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, cancel.StatusCode);
        Assert.Equal("interrupted", current.GetProperty("status").GetString());
        var runId = current.GetProperty("runId").GetString()!;
        Assert.Equal(RunStatus.Interrupted, new RunStore(_factory.DataDir).Load(runId)!.Status);
    }

    [Fact]
    public async Task Run_WithSettingsNotReadyForARun_Is400()
    {
        var response = await _factory.CreateAppClient().PostAsync("/api/runs", Json("""{"mode":"dry"}"""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("area.country", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("does-not-exist")]
    [InlineData("..%2Fsettings")]
    public async Task RunDetail_UnknownOrUnsafeId_Is404(string runId)
    {
        var response = await _factory.CreateClient().GetAsync($"/api/runs/{runId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // --- pending and decisions

    [Fact]
    public async Task NormalRun_ProducesPending_AndADecisionRemovesIt()
    {
        _factory.WriteSettings(JobbbyWebFactory.RunnableSettings());
        _factory.Source = new MockJobSource(_ => new[] { Posting("https://x/1") });
        var client = _factory.CreateAppClient();

        await client.PostAsync("/api/runs", Json("""{"mode":"normal"}"""));
        await client.GetStringAsync("/api/runs/current/events");
        var pending = await client.GetFromJsonAsync<JsonElement>("/api/pending");
        var postingId = pending.EnumerateArray().Single().GetProperty("postingId").GetString()!;

        var decision = await client.PostAsync($"/api/postings/{postingId}/decision", Json("""{"decision":"approve"}"""));
        var after = await client.GetFromJsonAsync<JsonElement>("/api/pending");

        Assert.Equal(HttpStatusCode.OK, decision.StatusCode);
        Assert.Empty(after.EnumerateArray());
        Assert.Equal(ApplicationOutcomes.Approved, new ApplicationLedger.ApplicationLedger(_factory.DataDir.ApplicationsPath).Current(postingId)!.Outcome);
    }

    [Fact]
    public async Task Decision_UnknownPosting_Is404_UnknownDecision_Is400()
    {
        var client = _factory.CreateAppClient();

        var unknownPosting = await client.PostAsync("/api/postings/0123456789abcdef0123456789abcdef/decision", Json("""{"decision":"approve"}"""));
        var unknownDecision = await client.PostAsync("/api/postings/0123456789abcdef0123456789abcdef/decision", Json("""{"decision":"maybe"}"""));

        Assert.Equal(HttpStatusCode.NotFound, unknownPosting.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unknownDecision.StatusCode);
    }

    [Fact]
    public async Task DecisionDuringARun_IsNotLost()
    {
        var key = PostingIdentity.Key("Earlier", "Cook");
        new ApplicationLedger.ApplicationLedger(_factory.DataDir.ApplicationsPath).RecordOutcome(
            new ApplicationRecord(key, "Earlier", "Cook", "https://y/1", DateTimeOffset.UtcNow, ApplicationOutcomes.Pending, PostingIdentity.Id(key)));
        _factory.WriteSettings(JobbbyWebFactory.RunnableSettings());
        _factory.Source = new MockJobSource(_ => new[] { Posting("https://x/1") });
        var release = new TaskCompletionSource();
        var blocked = new TaskCompletionSource();
        _factory.Llm = new PromptRoutedLlm(async token =>
        {
            blocked.TrySetResult();
            await release.Task.WaitAsync(token);
        });
        var client = _factory.CreateAppClient();

        await client.PostAsync("/api/runs", Json("""{"mode":"normal"}"""));
        await blocked.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var decision = await client.PostAsync($"/api/postings/{PostingIdentity.Id(key)}/decision", Json("""{"decision":"reject"}"""));
        release.SetResult();
        await client.GetStringAsync("/api/runs/current/events");

        Assert.Equal(HttpStatusCode.OK, decision.StatusCode);
        var ledger = new ApplicationLedger.ApplicationLedger(_factory.DataDir.ApplicationsPath);
        Assert.Equal(ApplicationOutcomes.Rejected, ledger.Current(PostingIdentity.Id(key))!.Outcome);
        Assert.Single(ledger.Pending()); // the run's own posting, written after the decision without erasing it
    }

    private static RawPosting Posting(string url) => new("Nurse", "Pronto soccorso", url, "api.adzuna.com", "Clinic", DateTimeOffset.UtcNow.AddHours(-1));

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    public void Dispose() => _factory.Dispose();
}
