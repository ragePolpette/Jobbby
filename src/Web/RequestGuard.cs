namespace Web;

/// <summary>
/// Protects state-changing requests from other sites. The UI sends <see cref="HeaderName"/> on
/// every POST/PUT/PATCH/DELETE: a custom header forces a CORS preflight, which this app never
/// grants to other origins, so a foreign page (including a multipart form, a "simple request"
/// otherwise) cannot send it. A present <c>Origin</c> must also be the app's own. DNS rebinding
/// is covered separately by <c>AllowedHosts</c>.
/// </summary>
public static class RequestGuard
{
    public const string HeaderName = "X-Jobbby-Request";

    public static IApplicationBuilder UseRequestGuard(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var request = context.Request;
            if (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method) || HttpMethods.IsOptions(request.Method))
            {
                await next();
                return;
            }

            if (request.Headers[HeaderName] != "1")
            {
                await Reject(context, $"Richiesta rifiutata: manca l'header {HeaderName}.");
                return;
            }

            var origin = request.Headers.Origin.ToString();
            if (origin.Length > 0 && !string.Equals(origin, $"{request.Scheme}://{request.Host}", StringComparison.OrdinalIgnoreCase))
            {
                await Reject(context, "Richiesta rifiutata: origine diversa da quella dell'app.");
                return;
            }

            await next();
        });

    private static Task Reject(HttpContext context, string message)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return context.Response.WriteAsJsonAsync(new { error = message });
    }
}
