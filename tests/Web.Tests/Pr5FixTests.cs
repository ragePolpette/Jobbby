using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Web.Tests;

/// <summary>Regressions found checking the PR 4 and PR 5 pages in a real browser.</summary>
public class Pr5FixTests : IDisposable
{
    private readonly JobbbyWebFactory _factory = new();

    [Fact]
    public async Task SecretsStatus_ClaudeCli_DoesNotListTheOpenAiSecrets()
    {
        _factory.WriteSettings(JobbbyWebFactory.RunnableSettings());

        var status = await _factory.CreateClient().GetFromJsonAsync<Dictionary<string, JsonElement>>("/api/secrets/status");

        Assert.Contains("Adzuna:AppKey", status!.Keys);
        Assert.DoesNotContain("Llm:Endpoint", status.Keys);
        Assert.DoesNotContain("Llm:ApiKey", status.Keys);
    }

    [Fact]
    public async Task SecretsStatus_OpenAi_ListsTheOpenAiSecrets()
    {
        var settings = JobbbyWebFactory.RunnableSettings();
        _factory.WriteSettings(settings with { Llm = settings.Llm with { Provider = "openai" } });

        var status = await _factory.CreateClient().GetFromJsonAsync<Dictionary<string, JsonElement>>("/api/secrets/status");

        Assert.Contains("Llm:Endpoint", status!.Keys);
        Assert.Contains("Llm:ApiKey", status.Keys);
    }

    [Fact]
    public async Task Page_HasAnInlineFavicon()
    {
        var html = await _factory.CreateClient().GetStringAsync("/");

        Assert.Contains("rel=\"icon\"", html);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/app.js")]
    [InlineData("/app.css")]
    public async Task StaticFiles_AreRevalidated_SoAnUpdateIsSeenWithoutAHardReload(string path)
    {
        var response = await _factory.CreateClient().GetAsync(path);

        Assert.Contains("no-cache", response.Headers.CacheControl?.ToString() ?? "");
    }

    [Fact]
    public async Task Script_NeverAppendsARawTernaryNull()
    {
        // Node.append(null) writes the text "null": optional children go through el(), which skips them.
        var script = await _factory.CreateClient().GetStringAsync("/app.js");

        Assert.DoesNotMatch(new System.Text.RegularExpressions.Regex(@"clear\([^)]*\)\.append\([^;]*: null"), script);
    }

    public void Dispose() => _factory.Dispose();
}
