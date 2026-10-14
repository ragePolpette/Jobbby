using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace GraphEngine;

/// <summary>
/// Chat completions against any OpenAI-compatible endpoint. At most <c>maxConcurrency</c>
/// requests are in flight per instance; transient failures (408, 429, 5xx, timeouts) are
/// retried with exponential backoff and jitter, waiting the server's Retry-After when it sends
/// one. A Retry-After longer than <c>maxRetryAfter</c> is not waited out: the call fails.
/// </summary>
public sealed class OpenAiCompatibleLlmClient : ILlmClient, IDisposable
{
    public const int DefaultMaxConcurrency = 4;

    private readonly HttpClient _httpClient;
    private readonly Uri _endpoint;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly int _maxAttempts;
    private readonly TimeSpan _requestTimeout;
    private readonly TimeSpan _retryDelay;
    private readonly TimeSpan _maxRetryDelay;
    private readonly TimeSpan _maxRetryAfter;
    private readonly SemaphoreSlim _concurrency;
    private readonly TimeProvider _timeProvider;

    public OpenAiCompatibleLlmClient(
        HttpClient httpClient,
        string endpoint,
        string apiKey,
        string model,
        int maxAttempts = 3,
        TimeSpan? requestTimeout = null,
        TimeSpan? retryDelay = null,
        int maxConcurrency = DefaultMaxConcurrency,
        TimeSpan? maxRetryAfter = null,
        TimeProvider? timeProvider = null)
    {
        _httpClient = httpClient;
        _endpoint = new Uri(endpoint, UriKind.Absolute);
        _apiKey = apiKey;
        _model = model;
        _maxAttempts = Math.Max(1, maxAttempts);
        _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(30);
        _retryDelay = retryDelay ?? TimeSpan.FromMilliseconds(500);
        _maxRetryDelay = TimeSpan.FromSeconds(30);
        _maxRetryAfter = maxRetryAfter ?? TimeSpan.FromSeconds(60);
        MaxConcurrency = Math.Max(1, maxConcurrency);
        _concurrency = new SemaphoreSlim(MaxConcurrency);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public int MaxConcurrency { get; }

    public async Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken = default)
    {
        Exception? lastError = null;
        for (var attempt = 1; attempt <= _maxAttempts; attempt++)
        {
            TimeSpan? retryAfter = null;
            await _concurrency.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await SendAsync(prompt, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsRetryable(ex, cancellationToken) && attempt < _maxAttempts)
            {
                lastError = ex;
                retryAfter = (ex as TransientResponseException)?.RetryAfter;
            }
            catch (Exception ex) when (ex is not LlmException)
            {
                throw new LlmException("The LLM request failed.", ex);
            }
            finally
            {
                _concurrency.Release();
            }

            if (retryAfter > _maxRetryAfter)
                throw new LlmException(
                    $"The LLM endpoint asked to wait {retryAfter.Value.TotalSeconds:0}s before retrying, more than the {_maxRetryAfter.TotalSeconds:0}s allowed.",
                    lastError);

            await Task.Delay(RetryBackoff.Delay(attempt, _retryDelay, _maxRetryDelay, retryAfter), _timeProvider, cancellationToken).ConfigureAwait(false);
        }

        throw new LlmException($"The LLM request failed after {_maxAttempts} attempts.", lastError);
    }

    public void Dispose() => _concurrency.Dispose();

    private async Task<string> SendAsync(string prompt, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new
        {
            model = _model,
            messages = new[] { new { role = "user", content = prompt } },
            temperature = 0,
        });

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_requestTimeout);
        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request, timeout.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            if (RetryBackoff.IsTransient(response.StatusCode))
                throw new TransientResponseException(response.StatusCode, RetryBackoff.RetryAfter(response, _timeProvider));

            throw new LlmException($"LLM request was rejected with HTTP {(int)response.StatusCode}.");
        }

        try
        {
            var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token).ConfigureAwait(false);
            var content = document.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            return string.IsNullOrWhiteSpace(content)
                ? throw new LlmException("The LLM response did not contain message content.")
                : content;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or IndexOutOfRangeException or InvalidOperationException)
        {
            throw new LlmException("The LLM response was not valid JSON.", ex);
        }
    }

    private static bool IsRetryable(Exception exception, CancellationToken callerToken) =>
        exception is TransientResponseException ||
        exception is HttpRequestException { StatusCode: null } ||
        exception is TaskCanceledException && !callerToken.IsCancellationRequested;

    private sealed class TransientResponseException(HttpStatusCode statusCode, TimeSpan? retryAfter)
        : HttpRequestException($"Transient LLM response: {(int)statusCode}.", null, statusCode)
    {
        public TimeSpan? RetryAfter { get; } = retryAfter;
    }
}
