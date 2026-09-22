using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace GraphEngine;

public sealed class OpenAiCompatibleLlmClient : ILlmClient
{
    private readonly HttpClient _httpClient;
    private readonly Uri _endpoint;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly int _maxAttempts;
    private readonly TimeSpan _requestTimeout;
    private readonly TimeSpan _retryDelay;

    public OpenAiCompatibleLlmClient(
        HttpClient httpClient,
        string endpoint,
        string apiKey,
        string model,
        int maxAttempts = 3,
        TimeSpan? requestTimeout = null,
        TimeSpan? retryDelay = null)
    {
        _httpClient = httpClient;
        _endpoint = new Uri(endpoint, UriKind.Absolute);
        _apiKey = apiKey;
        _model = model;
        _maxAttempts = Math.Max(1, maxAttempts);
        _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(30);
        _retryDelay = retryDelay ?? TimeSpan.FromMilliseconds(250);
    }

    public async Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken = default)
    {
        Exception? lastError = null;
        for (var attempt = 1; attempt <= _maxAttempts; attempt++)
        {
            try
            {
                return await SendAsync(prompt, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsRetryable(ex, cancellationToken) && attempt < _maxAttempts)
            {
                lastError = ex;
                await Task.Delay(_retryDelay * attempt, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not LlmException)
            {
                throw new LlmException("The LLM request failed.", ex);
            }
        }

        throw new LlmException($"The LLM request failed after {_maxAttempts} attempts.", lastError);
    }

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
            if (IsTransient(response.StatusCode))
                throw new HttpRequestException($"Transient LLM response: {(int)response.StatusCode}.", null, response.StatusCode);

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
        catch (JsonException ex)
        {
            throw new LlmException("The LLM response was not valid JSON.", ex);
        }
    }

    private static bool IsRetryable(Exception exception, CancellationToken callerToken) =>
        exception is HttpRequestException requestException &&
            (requestException.StatusCode is null || IsTransient(requestException.StatusCode.Value)) ||
        exception is TaskCanceledException && !callerToken.IsCancellationRequested;

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests ||
        (int)statusCode >= 500;
}
