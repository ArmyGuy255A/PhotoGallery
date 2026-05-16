namespace PhotoGallery.Middleware;

/// <summary>
/// Writes <see cref="HttpContext.TraceIdentifier"/> to the <c>X-Trace-Id</c>
/// response header on every request. The SPA captures this header on both
/// success and failure responses so user-reported issues can be correlated to
/// backend logs without depending on the request having thrown.
///
/// Gated to <c>/api/</c> paths to avoid leaking the trace id on static-asset /
/// Razor / SignalR responses that are not user-actionable diagnostics surfaces.
/// </summary>
public class TraceIdHeaderMiddleware
{
    public const string HeaderName = "X-Trace-Id";

    private readonly RequestDelegate _next;

    public TraceIdHeaderMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public Task InvokeAsync(HttpContext context)
    {
        // Write eagerly so test harnesses (DefaultHttpContext, no real server)
        // and the production pipeline behave identically. OnStarting was the
        // first attempt but never fires in DefaultHttpContext. The header is
        // re-asserted by ExceptionHandlingMiddleware in its rewrite path.
        if (!context.Response.Headers.ContainsKey(HeaderName))
        {
            context.Response.Headers[HeaderName] = context.TraceIdentifier;
        }

        return _next(context);
    }
}
