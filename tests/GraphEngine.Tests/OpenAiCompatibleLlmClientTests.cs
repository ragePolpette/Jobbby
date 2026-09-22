using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;

namespace GraphEngine.Tests;

public class OpenAiCompatibleLlmClientTests
{
    [Fact]
    public async Task CompleteAsync_SendsChatCompletionAndReturnsContent()
    {
        var handler = new ScriptedHandler(Ok("{\"ok\":true}"));
        using var httpClient = new HttpClient(handler);
        var client = NewClient(httpClient);

        var result = await client.CompleteAsync("test prompt");

        Assert.Equal("{\"ok\":true}", result);
        Assert.Equal("Bearer", handler.LastRequest!.Headers.Authorization!.Scheme);
        Assert.Equal("secret", handler.LastRequest.Headers.Authorization.Parameter);
        using var body = JsonDocument.Parse(handler.LastBody!);
        Assert.Equal("test-model", body.RootElement.GetProperty("model").GetString());
        Assert.Equal("test prompt", body.RootElement.GetProperty("messages")[0].GetProperty("content").GetString());
    }

    [Fact]
    public async Task CompleteAsync_TransientFailure_RetriesThenSucceeds()
    {
        var handler = new ScriptedHandler(
            new HttpResponseMessage(HttpStatusCode.TooManyRequests),
            Ok("done"));
        using var httpClient = new HttpClient(handler);

        var result = await NewClient(httpClient).CompleteAsync("prompt");

        Assert.Equal("done", result);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task CompleteAsync_InvalidProviderJson_ThrowsTypedExceptionWithoutRetrying()
    {
        var handler = new ScriptedHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("not json"),
        });
        using var httpClient = new HttpClient(handler);

        var error = await Assert.ThrowsAsync<LlmException>(() => NewClient(httpClient).CompleteAsync("prompt"));

        Assert.Contains("not valid JSON", error.Message);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task CompleteAsync_RequestTimesOut_RetriesAndFailsWithTypedException()
    {
        var handler = new ScriptedHandler(_ => Task.Delay(Timeout.InfiniteTimeSpan, _));
        using var httpClient = new HttpClient(handler);
        var client = NewClient(httpClient, maxAttempts: 2, timeout: TimeSpan.FromMilliseconds(20));

        await Assert.ThrowsAsync<LlmException>(() => client.CompleteAsync("prompt"));
        Assert.Equal(2, handler.RequestCount);
    }

    private static OpenAiCompatibleLlmClient NewClient(
        HttpClient httpClient,
        int maxAttempts = 3,
        TimeSpan? timeout = null) =>
        new(httpClient, "https://llm.example/v1/chat/completions", "secret", "test-model",
            maxAttempts, timeout, TimeSpan.Zero);

    private static HttpResponseMessage Ok(string content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(
            JsonSerializer.Serialize(new { choices = new[] { new { message = new { content } } } }),
            Encoding.UTF8,
            "application/json"),
    };

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Queue<Func<CancellationToken, Task<HttpResponseMessage>>> _responses;

        public ScriptedHandler(params HttpResponseMessage[] responses)
            : this(responses.Select<HttpResponseMessage, Func<CancellationToken, Task<HttpResponseMessage>>>(
                response => _ => Task.FromResult(response)).ToArray())
        {
        }

        public ScriptedHandler(params Func<CancellationToken, Task>[] responses)
            : this(responses.Select<Func<CancellationToken, Task>, Func<CancellationToken, Task<HttpResponseMessage>>>(
                response => async token =>
                {
                    await response(token);
                    return Ok(string.Empty);
                }).ToArray())
        {
        }

        private ScriptedHandler(params Func<CancellationToken, Task<HttpResponseMessage>>[] responses)
        {
            _responses = new Queue<Func<CancellationToken, Task<HttpResponseMessage>>>(responses);
        }

        public int RequestCount { get; private set; }
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            LastRequest = request;
            LastBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return await _responses.Dequeue()(cancellationToken);
        }
    }
}
