using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ApplicationLedger;
using Host;
using Xunit;

namespace Web.Tests;

/// <summary>Regressions for the PR 4 final review.</summary>
public class Pr4ReviewFixTests : IDisposable
{
    private readonly JobbbyWebFactory _factory = new();

    [Fact]
    public void LedgerHolder_SuffixChange_RekeysTheSameInstance_NeverASecondOne()
    {
        var holder = new LedgerHolder(_factory.DataDir);
        var before = holder.Get(Array.Empty<string>());
        before.RecordOutcome(new ApplicationRecord(PostingIdentity.Key("Acme Holding", "Cuoco"), "Acme Holding", "Cuoco", null, DateTimeOffset.UtcNow, ApplicationOutcomes.Pending));

        var after = holder.Get(new[] { "Holding" });
        var again = holder.Get(new[] { " holding " });

        Assert.Same(before, after);
        Assert.Same(after, again);
        Assert.True(after.HasBeenProcessed(PostingIdentity.Key("Acme", "Cuoco", new[] { "holding" })));
    }

    [Theory]
    [InlineData("""{"evaluation":{"autoApproveThreshold":null}}""", "evaluation.autoApproveThreshold")]
    [InlineData("""{"salary":{"minimumPlausible":null}}""", "salary.minimumPlausible")]
    public async Task Settings_EmptyNumberSentAsNull_Is400OnTheField_NotSavedAsZero(string body, string field)
    {
        var response = await _factory.CreateAppClient().PutAsync("/api/settings", new StringContent(body, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(field, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Script_SendsEmptyNumbersAsNull_AndShowsUnmatchedErrors()
    {
        var script = await _factory.CreateClient().GetStringAsync("/app.js");

        Assert.DoesNotMatch(new Regex(@"\bNumber\(value\("), script);
        Assert.Contains("unmatched", script);
    }

    [Fact]
    public async Task CorruptSettingsFile_GivesAReadableJsonError()
    {
        File.WriteAllText(_factory.DataDir.SettingsPath, """{"area": """);

        var response = await _factory.CreateClient().GetAsync("/api/settings");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var error = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString();
        Assert.Contains("settings.json", error);
    }

    [Fact]
    public async Task Pages_CannotBeFramed_AndAreNotSniffed()
    {
        var response = await _factory.CreateClient().GetAsync("/");

        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Contains("frame-ancestors 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
    }

    public void Dispose() => _factory.Dispose();
}
