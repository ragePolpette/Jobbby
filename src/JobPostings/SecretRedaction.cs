using System.Text.RegularExpressions;

namespace JobPostings;

/// <summary>Masks credentials that travel in query strings (Adzuna's app_id/app_key) wherever a URL may end up in text.</summary>
public static class SecretRedaction
{
    public const string Mask = "***";

    private static readonly Regex SecretParameter = new(
        @"(?<name>\b(?:app_id|app_key|api_key|apikey|access_token|token)=)[^&#\s""'<>()\[\],;]*",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string Redact(string? text) =>
        string.IsNullOrEmpty(text) ? string.Empty : SecretParameter.Replace(text, "${name}" + Mask);
}

/// <summary>
/// Rethrows any failure of the inner pipeline with credentials masked in the message and
/// without the original exception chain, whose messages may repeat the request URL.
/// </summary>
public sealed class SecretRedactingHandler : DelegatingHandler
{
    public SecretRedactingHandler()
    {
    }

    public SecretRedactingHandler(HttpMessageHandler innerHandler)
        : base(innerHandler)
    {
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new HttpRequestException(SecretRedaction.Redact(ex.Message), null, ex.StatusCode);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && SecretRedaction.Redact(ex.Message) != ex.Message)
        {
            throw new HttpRequestException(SecretRedaction.Redact(ex.Message));
        }
    }
}
