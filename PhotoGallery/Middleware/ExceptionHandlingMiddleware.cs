using System.Text.Json;

namespace PhotoGallery.Middleware;

/// <summary>
/// Global exception handler for the API surface. Catches anything that escapes
/// downstream middleware / endpoints, maps it to a known status code, and
/// serializes a uniform <see cref="ApiErrorResponse"/> envelope.
///
/// Gated to <c>/api/</c> paths so the legacy MVC + Razor surfaces keep using
/// <c>app.UseExceptionHandler("/Home/Error")</c> unchanged.
///
/// Status-code mapping:
/// <list type="bullet">
///   <item><see cref="ArgumentException"/> → 400 BadRequest</item>
///   <item><see cref="KeyNotFoundException"/> → 404 NotFound</item>
///   <item><see cref="UnauthorizedAccessException"/> → 403 Forbidden</item>
///   <item><see cref="OperationCanceledException"/> → 499 ClientClosedRequest</item>
///   <item>Anything else → 500 InternalServerError</item>
/// </list>
/// </summary>
public class ExceptionHandlingMiddleware
{
    /// <summary>Non-standard but widely-used nginx code for a client-closed connection.</summary>
    public const int StatusClientClosedRequest = 499;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly IHostEnvironment _env;

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger,
        IHostEnvironment env)
    {
        _next = next;
        _logger = logger;
        _env = env;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleAsync(context, ex);
        }
    }

    private async Task HandleAsync(HttpContext context, Exception ex)
    {
        var (status, code, message) = Map(ex);
        var traceId = context.TraceIdentifier;

        // Structured log: traceId is a first-class property so operators can
        // grep / KQL-query by the id the SPA captures in its error toast.
        // Cancellation is a client behaviour, not an app fault, so it logs as
        // Information rather than Error.
        if (status == StatusClientClosedRequest)
        {
            _logger.LogInformation(
                "Request cancelled by client. TraceId={TraceId} Path={Path}",
                traceId, context.Request.Path);
        }
        else
        {
            _logger.LogError(
                ex,
                "Unhandled exception. TraceId={TraceId} Status={Status} Code={Code} Path={Path}",
                traceId, status, code, context.Request.Path);
        }

        if (context.Response.HasStarted)
        {
            // Headers already flushed to the wire. We can't rewrite the
            // response; let the connection close. The log line above is the
            // only diagnostic the operator gets.
            return;
        }

        // Set the final status + content type without nuking already-written
        // response headers (notably X-Trace-Id, set by TraceIdHeaderMiddleware
        // upstream). Response.Clear() would drop them.
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.ContentLength = null;

        object? details = null;
        if (IsDiagnosticEnvironment(_env))
        {
            details = new
            {
                exception = ex.GetType().Name,
                stackTrace = ex.ToString()
            };
        }

        var body = new ApiErrorResponse
        {
            Code = code,
            Message = message,
            TraceId = traceId,
            Details = details
        };

        await JsonSerializer.SerializeAsync(context.Response.Body, body, JsonOptions);
    }

    /// <summary>
    /// Map an exception to a (status, code, user-safe message) triple. The
    /// message intentionally omits the raw <c>ex.Message</c> in the default
    /// path so internal phrasing never leaks to clients. The exception's own
    /// message is surfaced only for <see cref="ArgumentException"/> /
    /// <see cref="KeyNotFoundException"/> where the developer-supplied text is
    /// already a validation-style summary, never an internal type detail.
    /// </summary>
    internal static (int Status, string Code, string Message) Map(Exception ex)
    {
        return ex switch
        {
            ArgumentException arg => (
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.BadRequest,
                string.IsNullOrWhiteSpace(arg.Message) ? "The request was invalid." : arg.Message),

            KeyNotFoundException knf => (
                StatusCodes.Status404NotFound,
                ApiErrorCodes.NotFound,
                string.IsNullOrWhiteSpace(knf.Message) ? "The requested resource was not found." : knf.Message),

            UnauthorizedAccessException => (
                StatusCodes.Status403Forbidden,
                ApiErrorCodes.Forbidden,
                "You do not have permission to perform this action."),

            OperationCanceledException => (
                StatusClientClosedRequest,
                ApiErrorCodes.ClientClosedRequest,
                "The request was cancelled by the client."),

            _ => (
                StatusCodes.Status500InternalServerError,
                ApiErrorCodes.InternalServerError,
                "An unexpected error occurred. Reference the traceId when reporting this issue.")
        };
    }

    /// <summary>
    /// Development + Trial expose stack traces in the response body so the
    /// dev team can debug live without tailing logs. Staging / Production
    /// never leak internals.
    /// </summary>
    internal static bool IsDiagnosticEnvironment(IHostEnvironment env) =>
        env.IsDevelopment() ||
        string.Equals(env.EnvironmentName, "Trial", StringComparison.OrdinalIgnoreCase);
}
