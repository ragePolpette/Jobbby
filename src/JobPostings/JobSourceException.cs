using System.Net;

namespace JobPostings;

/// <summary>A job source request that failed. The message never contains the request URL or credentials.</summary>
public sealed class JobSourceException : Exception
{
    public JobSourceException(string message, HttpStatusCode? statusCode = null, bool isTransient = false, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        IsTransient = isTransient;
    }

    public HttpStatusCode? StatusCode { get; }

    /// <summary>True for failures worth retrying: 408, 429, 5xx, timeouts and network errors.</summary>
    public bool IsTransient { get; }
}
