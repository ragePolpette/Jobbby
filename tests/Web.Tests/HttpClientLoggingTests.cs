using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Web.Tests;

/// <summary>Adzuna puts app_id/app_key in the query string: nothing the outgoing client does may write them anywhere.</summary>
public class HttpClientLoggingTests
{
    private const string Url = "https://api.adzuna.com/v1/api/jobs/de/search/1?app_id=ID-0001&app_key=KEY-0001&what=nurse";

    [Fact]
    public async Task OutgoingRequests_AreNotLogged_SoTheQueryStringCredentialsNeverReachTheLogs()
    {
        using var factory = new JobbbyWebFactory();
        var sink = new CapturingLoggerProvider();
        using var app = factory.WithWebHostBuilder(builder => builder
            .ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(sink))
            .ConfigureServices(services => services.AddHttpClient(JobbbyWeb.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => new FixedHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)))));
        var client = app.Services.GetRequiredService<IHttpClientFactory>().CreateClient(JobbbyWeb.HttpClientName);

        await client.GetAsync(Url);

        Assert.DoesNotContain(sink.Messages, message => message.Contains("KEY-0001") || message.Contains("ID-0001"));
    }

    [Fact]
    public async Task OutgoingFailures_CarryNoCredentials()
    {
        using var factory = new JobbbyWebFactory();
        using var app = factory.WithWebHostBuilder(builder => builder
            .ConfigureServices(services => services.AddHttpClient(JobbbyWeb.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => new FixedHandler(request => throw new HttpRequestException($"refused: {request.RequestUri}")))));
        var client = app.Services.GetRequiredService<IHttpClientFactory>().CreateClient(JobbbyWeb.HttpClientName);

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(Url));

        Assert.DoesNotContain("KEY-0001", error.ToString());
    }

    private sealed class FixedHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Logger(categoryName, Messages);

        public void Dispose()
        {
        }

        private sealed class Logger(string category, ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
            {
                messages.Enqueue($"{category} scope: {state}");
                return null;
            }

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                messages.Enqueue($"{category}: {formatter(state, exception)} {exception}");
        }
    }
}
