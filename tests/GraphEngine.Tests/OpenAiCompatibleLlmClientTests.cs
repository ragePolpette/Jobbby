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

    [Fact]
    public async Task CompleteAsync_WaitsTheServersRetryAfter_BeforeRetrying()
    {
        var limited = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        limited.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(7));
        var handler = new ScriptedHandler(limited, Ok("done"));
        using var httpClient = new HttpClient(handler);
        var time = new RecordingTimeProvider();
        var client = new OpenAiCompatibleLlmClient(httpClient, "https://llm.example/v1/chat/completions", "secret", "test-model",
            retryDelay: TimeSpan.FromMilliseconds(100), timeProvider: time);

        var result = await client.CompleteAsync("prompt");

        Assert.Equal("done", result);
        Assert.Equal(TimeSpan.FromSeconds(7), Assert.Single(time.Delays));
    }

    [Fact]
    public async Task CompleteAsync_RetryAfterBeyondTheLimit_FailsWithoutWaiting()
    {
        var limited = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        limited.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMinutes(10));
        var handler = new ScriptedHandler(limited, Ok("done"));
        using var httpClient = new HttpClient(handler);
        var time = new RecordingTimeProvider();
        var client = new OpenAiCompatibleLlmClient(httpClient, "https://llm.example/v1/chat/completions", "secret", "test-model",
            maxRetryAfter: TimeSpan.FromSeconds(30), timeProvider: time);

        var error = await Assert.ThrowsAsync<LlmException>(() => client.CompleteAsync("prompt"));

        Assert.Contains("600s", error.Message);
        Assert.Equal(1, handler.RequestCount);
        Assert.Empty(time.Delays);
    }

    [Fact]
    public async Task CompleteAsync_BacksOffExponentiallyWithJitter()
    {
        var handler = new ScriptedHandler(
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            new HttpResponseMessage(HttpStatusCode.BadGateway),
            new HttpResponseMessage(HttpStatusCode.InternalServerError),
            Ok("done"));
        using var httpClient = new HttpClient(handler);
        var time = new RecordingTimeProvider();
        var client = new OpenAiCompatibleLlmClient(httpClient, "https://llm.example/v1/chat/completions", "secret", "test-model",
            maxAttempts: 4, retryDelay: TimeSpan.FromMilliseconds(100), timeProvider: time);

        Assert.Equal("done", await client.CompleteAsync("prompt"));

        var delays = time.Delays.Select(delay => delay.TotalMilliseconds).ToList();
        Assert.Equal(3, delays.Count);
        Assert.InRange(delays[0], 50, 100);
        Assert.InRange(delays[1], 100, 200);
        Assert.InRange(delays[2], 200, 400);
    }

    [Fact]
    public async Task CompleteAsync_NeverExceedsMaxConcurrency()
    {
        var handler = new ConcurrencyProbe();
        using var httpClient = new HttpClient(handler);
        var client = new OpenAiCompatibleLlmClient(httpClient, "https://llm.example/v1/chat/completions", "secret", "test-model",
            maxConcurrency: 2);

        var calls = Enumerable.Range(0, 10).Select(i => client.CompleteAsync($"prompt {i}")).ToList();
        await Task.WhenAll(calls);

        Assert.Equal(2, handler.MaxInFlight);
        Assert.Equal(10, handler.Total);
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

    private sealed class ConcurrencyProbe : HttpMessageHandler
    {
        private int _inFlight;
        private int _maxInFlight;
        private int _total;

        public int MaxInFlight => Volatile.Read(ref _maxInFlight);
        public int Total => Volatile.Read(ref _total);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var now = Interlocked.Increment(ref _inFlight);
            int seen;
            while (now > (seen = Volatile.Read(ref _maxInFlight)) && Interlocked.CompareExchange(ref _maxInFlight, now, seen) != seen)
            {
            }

            await Task.Delay(20, cancellationToken);
            Interlocked.Decrement(ref _inFlight);
            Interlocked.Increment(ref _total);
            return Ok("ok");
        }
    }
}
