using System.Net;

namespace GraphEngine;

/// <summary>Retry timing shared by the HTTP clients: exponential backoff with jitter, and the server's Retry-After.</summary>
public static class RetryBackoff
{
    /// <summary>
    /// The wait before retry number <paramref name="attempt"/> (1 = first retry): the server's
    /// <paramref name="retryAfter"/> when given, otherwise a random delay between half and all of
    /// <c>baseDelay * 2^(attempt-1)</c>, capped at <paramref name="maxDelay"/>.
    /// </summary>
    public static TimeSpan Delay(int attempt, TimeSpan baseDelay, TimeSpan maxDelay, TimeSpan? retryAfter = null)
    {
        if (retryAfter is { } serverDelay)
            return serverDelay < TimeSpan.Zero ? TimeSpan.Zero : serverDelay;

        var exponential = baseDelay.TotalMilliseconds * Math.Pow(2, Math.Max(0, attempt - 1));
        var capped = Math.Min(exponential, maxDelay.TotalMilliseconds);
        return TimeSpan.FromMilliseconds((capped / 2) + (Random.Shared.NextDouble() * capped / 2));
    }

    /// <summary>The response's Retry-After as a delay from now, or null when absent.</summary>
    public static TimeSpan? RetryAfter(HttpResponseMessage response, TimeProvider? timeProvider = null)
    {
        var header = response.Headers.RetryAfter;
        if (header?.Delta is { } delta)
            return delta < TimeSpan.Zero ? TimeSpan.Zero : delta;
        if (header?.Date is { } date)
        {
            var wait = date - (timeProvider ?? TimeProvider.System).GetUtcNow();
            return wait < TimeSpan.Zero ? TimeSpan.Zero : wait;
        }

        return null;
    }

    public static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int)statusCode >= 500;
}
