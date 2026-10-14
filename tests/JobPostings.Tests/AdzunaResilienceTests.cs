using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Config;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace JobPostings.Tests;

public class AdzunaResilienceTests
{
    private const string AppKey = "super-secret-key";
    private const string AppId = "secret-app-id";
    private static readonly SourceDefinition Source = new() { Name = "Adzuna", BaseUrl = "https://api.adzuna.com" };
    private static readonly JobSearchRequest Request = new("nurse", null, null, SearchSweep.Local);

    private const string OneResult = """{"results":[{"id":"4519856154","title":"Nurse","redirect_url":"https://www.adzuna.de/land/ad/4519856154?se=x","company":{"display_name":""}}]}""";

    [Fact]
    public async Task TransientStatus_IsRetriedWithBackoff_ThenSucceeds()
    {
        var handler = new SequenceHandler(Status(HttpStatusCode.ServiceUnavailable), Status(HttpStatusCode.BadGateway), Ok(OneResult));
        var time = new RecordingTimeProvider();
        var source = NewSource(handler, new AdzunaRetryOptions(MaxAttempts: 3, BaseDelay: TimeSpan.FromSeconds(1), TimeProvider: time));

        var postings = await source.FetchAsync(Source, Request);

        Assert.Single(postings);
        Assert.Equal(3, handler.Requests);
        Assert.InRange(time.Delays[0].TotalMilliseconds, 500, 1000);
        Assert.InRange(time.Delays[1].TotalMilliseconds, 1000, 2000);
    }

    [Fact]
    public async Task TooManyRequests_WaitsTheRetryAfter()
    {
        var limited = Status(HttpStatusCode.TooManyRequests);
        limited.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(3));
        var handler = new SequenceHandler(limited, Ok(OneResult));
        var time = new RecordingTimeProvider();

        await NewSource(handler, new AdzunaRetryOptions(TimeProvider: time)).FetchAsync(Source, Request);

        Assert.Equal(TimeSpan.FromSeconds(3), Assert.Single(time.Delays));
    }

    [Fact]
    public async Task RetryAfterBeyondTheLimit_FailsAtOnce()
    {
        var limited = Status(HttpStatusCode.TooManyRequests);
        limited.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromHours(1));
        var handler = new SequenceHandler(limited, Ok(OneResult));
        var time = new RecordingTimeProvider();

        var error = await Assert.ThrowsAsync<JobSourceException>(() => NewSource(handler, new AdzunaRetryOptions(TimeProvider: time)).FetchAsync(Source, Request));

        Assert.Equal(HttpStatusCode.TooManyRequests, error.StatusCode);
        Assert.Equal(1, handler.Requests);
        Assert.Empty(time.Delays);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task PermanentStatus_IsNotRetried(HttpStatusCode status)
    {
        var handler = new SequenceHandler(Status(status), Ok(OneResult));

        var error = await Assert.ThrowsAsync<JobSourceException>(() => NewSource(handler).FetchAsync(Source, Request));

        Assert.Equal(1, handler.Requests);
        Assert.Equal(status, error.StatusCode);
        Assert.False(error.IsTransient);
        AssertNoSecrets(error);
    }

    [Fact]
    public async Task SlowResponse_TimesOutPerRequest_RetriesAndFailsWithDomainException()
    {
        var handler = new SequenceHandler(Hang, Hang);
        var source = NewSource(handler, new AdzunaRetryOptions(MaxAttempts: 2, RequestTimeout: TimeSpan.FromMilliseconds(50), TimeProvider: new RecordingTimeProvider()));

        var error = await Assert.ThrowsAsync<JobSourceException>(() => source.FetchAsync(Source, Request));

        Assert.Equal(2, handler.Requests);
        Assert.True(error.IsTransient);
        Assert.Contains("did not respond", error.Message);
    }

    [Fact]
    public async Task CallerCancellation_IsNotTurnedIntoARetryOrADomainError()
    {
        using var cancellation = new CancellationTokenSource();
        var handler = new SequenceHandler(async token =>
        {
            cancellation.Cancel();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Ok(OneResult);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => NewSource(handler).FetchAsync(Source, Request, cancellationToken: cancellation.Token));
        Assert.Equal(1, handler.Requests);
    }

    [Fact]
    public async Task NetworkError_CarryingTheUrl_IsReportedWithoutCredentials()
    {
        var handler = new SequenceHandler(
            request => throw new HttpRequestException($"Connection refused ({request.RequestUri})"),
            request => throw new HttpRequestException($"Connection refused ({request.RequestUri})"));
        var source = NewSource(handler, new AdzunaRetryOptions(MaxAttempts: 2, TimeProvider: new RecordingTimeProvider()));

        var error = await Assert.ThrowsAsync<JobSourceException>(() => source.FetchAsync(Source, Request));

        Assert.Equal(2, handler.Requests);
        AssertNoSecrets(error);
        Assert.Contains("app_key=***", error.Message);
    }

    [Fact]
    public async Task InvalidJson_IsADomainError()
    {
        var error = await Assert.ThrowsAsync<JobSourceException>(() => NewSource(new SequenceHandler(Ok("<html>"))).FetchAsync(Source, Request));

        Assert.IsAssignableFrom<System.Text.Json.JsonException>(error.InnerException);
    }

    [Fact]
    public async Task SourceId_IsReadFromTheResult()
    {
        var posting = Assert.Single(await NewSource(new SequenceHandler(Ok(OneResult))).FetchAsync(Source, Request));

        Assert.Equal("4519856154", posting.SourceId);
    }

    [Fact]
    public void FromConfiguration_ReadsTheSecretsFromTheInjectedConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Test:Id"] = AppId, ["Test:Key"] = AppKey })
            .Build();

        var source = AdzunaJobSource.FromConfiguration(new HttpClient(), configuration, "de", appIdSecretKey: "Test:Id", appKeySecretKey: "Test:Key");

        Assert.NotNull(source);
        var error = Assert.Throws<InvalidOperationException>(() =>
            AdzunaJobSource.FromConfiguration(new HttpClient(), new ConfigurationBuilder().Build(), "de", appIdSecretKey: "Missing:Id-" + Guid.NewGuid()));
        Assert.Contains("Missing secret", error.Message);
    }

    private static void AssertNoSecrets(Exception error)
    {
        for (var current = error; current is not null; current = current.InnerException)
        {
            Assert.DoesNotContain(AppKey, current.Message);
            Assert.DoesNotContain(AppId, current.Message);
        }
    }

    private static AdzunaJobSource NewSource(HttpMessageHandler handler, AdzunaRetryOptions? retry = null) =>
        new(new HttpClient(handler), AppId, AppKey, "de", retry: retry ?? new AdzunaRetryOptions(TimeProvider: new RecordingTimeProvider()));

    private static HttpResponseMessage Status(HttpStatusCode status) => new(status) { Content = new StringContent(string.Empty) };

    private static HttpResponseMessage Ok(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static async Task<HttpResponseMessage> Hang(CancellationToken token)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, token);
        return Ok(OneResult);
    }

    private sealed class SequenceHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _steps;
        private int _requests;

        public SequenceHandler(params HttpResponseMessage[] responses)
            => _steps = new(responses.Select(r => (Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>)((_, _) => Task.FromResult(r))));

        public SequenceHandler(params Func<CancellationToken, Task<HttpResponseMessage>>[] steps)
            => _steps = new(steps.Select(step => (Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>)((_, token) => step(token))));

        public SequenceHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] steps)
            => _steps = new(steps.Select(step => (Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>)((request, _) => Task.FromResult(step(request)))));

        public int Requests => Volatile.Read(ref _requests);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requests);
            return _steps.Dequeue()(request, cancellationToken);
        }
    }
}

public class SecretRedactionTests
{
    [Theory]
    [InlineData("GET https://api.adzuna.com/v1/api/jobs/de/search/1?app_id=abc&app_key=xyz&what=nurse", "GET https://api.adzuna.com/v1/api/jobs/de/search/1?app_id=***&app_key=***&what=nurse")]
    [InlineData("failed (https://x/?APP_KEY=xyz)", "failed (https://x/?APP_KEY=***)")]
    [InlineData("nothing secret here", "nothing secret here")]
    public void Redact_MasksQueryStringCredentials(string text, string expected)
    {
        Assert.Equal(expected, SecretRedaction.Redact(text));
    }

    [Fact]
    public async Task Handler_RethrowsFailuresWithoutTheCredentialsOrTheOriginalChain()
    {
        var inner = new ThrowingHandler(request => new HttpRequestException($"Boom at {request.RequestUri}", new IOException($"inner {request.RequestUri}")));
        using var client = new HttpClient(new SecretRedactingHandler(inner));

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("https://api.adzuna.com/search?app_id=ID1&app_key=KEY1"));

        Assert.DoesNotContain("KEY1", error.Message);
        Assert.DoesNotContain("ID1", error.Message);
        Assert.Null(error.InnerException);
    }

    private sealed class ThrowingHandler(Func<HttpRequestMessage, Exception> error) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(error(request));
    }
}
