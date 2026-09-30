using System.Net;
using System.Text;
using Host;
using Xunit;

namespace Web.Tests;

public class SecurityTests : IDisposable
{
    private readonly JobbbyWebFactory _factory = new();

    [Fact]
    public async Task ForeignHostHeader_IsRejected()
    {
        var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/settings");
        request.Headers.Host = "evil.example";

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1:5080")]
    public async Task LocalHostHeaders_AreAccepted(string host)
    {
        var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/settings");
        request.Headers.Host = host;

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Mutation_WithoutCustomHeader_IsForbidden()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync("/api/runs", Json("""{"mode":"dry"}"""));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task MultipartUpload_WithoutCustomHeader_IsForbidden()
    {
        var client = _factory.CreateClient();
        using var content = new MultipartFormDataContent { { new ByteArrayContent(new byte[] { 1 }), "file", "cv.pdf" } };

        var response = await client.PostAsync("/api/cv", content);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Mutation_FromAnotherOrigin_IsForbidden()
    {
        var client = _factory.CreateAppClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/runs") { Content = Json("""{"mode":"dry"}""") };
        request.Headers.Add("Origin", "https://evil.example");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Mutation_FromTheAppsOwnOrigin_PassesTheGuard()
    {
        var client = _factory.CreateAppClient();
        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/settings") { Content = Json("""{"area":{"distanceKm":-1}}""") };
        request.Headers.Add("Origin", "http://localhost");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); // reached validation: not blocked by the guard
    }

    [Fact]
    public async Task IndexPage_IsServed()
    {
        var response = await _factory.CreateClient().GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Jobbby", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public void SecondProcessOnTheSameDataDir_CannotStart()
    {
        _ = _factory.CreateClient();

        Assert.Throws<DataDirLockedException>(() => DataDirLock.Acquire(_factory.DataDir));
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    public void Dispose() => _factory.Dispose();
}
