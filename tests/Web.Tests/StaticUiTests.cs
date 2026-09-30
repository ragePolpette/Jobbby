using System.Net;
using System.Text.RegularExpressions;
using Xunit;

namespace Web.Tests;

public class StaticUiTests : IDisposable
{
    private readonly JobbbyWebFactory _factory = new();

    [Fact]
    public async Task Page_HasTheFourSections_AndTheAdzunaAttribution()
    {
        var html = await _factory.CreateClient().GetStringAsync("/");

        foreach (var section in new[] { "Run", "Impostazioni", "CV", "Storico" })
            Assert.Contains($">{section}<", html);
        Assert.Contains("Jobs by Adzuna", html);
        Assert.Contains("app.js", html);
        Assert.Contains("app.css", html);
    }

    [Theory]
    [InlineData("/app.js", "javascript")]
    [InlineData("/app.css", "css")]
    public async Task Assets_AreServed(string path, string contentType)
    {
        var response = await _factory.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(contentType, response.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Script_NeverInjectsHtml_AndSendsTheGuardHeader()
    {
        var script = await _factory.CreateClient().GetStringAsync("/app.js");

        Assert.DoesNotMatch(new Regex(@"\b(innerHTML|outerHTML|insertAdjacentHTML|document\.write)\b"), script);
        Assert.Contains("X-Jobbby-Request", script);
    }

    [Fact]
    public async Task CvPage_UploadsEditsExtractsAndPreviews_WithTheCurrentApiShape()
    {
        var script = await _factory.CreateClient().GetStringAsync("/app.js");

        Assert.Contains("new FormData()", script);
        Assert.Contains("\"/api/cv/extract\"", script);
        Assert.Contains("\"/api/cv/derived-queries\"", script);
        Assert.Contains("api(\"PUT\", \"/api/cv\"", script);
        // The GET /api/cv shape of the read-only page is gone.
        Assert.DoesNotContain("cv.file", script);
        Assert.DoesNotContain("cv.message", script);
    }

    public void Dispose() => _factory.Dispose();
}
