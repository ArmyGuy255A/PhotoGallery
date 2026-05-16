namespace PhotoGallery.Middleware;

/// <summary>
/// Uniform error envelope returned by the API for every non-2xx response.
///
/// Shape is deliberately small + stable: the SPA reads <c>code</c> for branching,
/// surfaces <c>message</c> in toasts, and includes <c>traceId</c> in user-reported
/// bug captures so backend logs are greppable.
///
/// <c>details</c> is populated only in Development / Trial so we never leak
/// stack traces or internal type names in Staging / Production.
/// </summary>
public sealed class ApiErrorResponse
{
    public string Code { get; init; } = ApiErrorCodes.InternalServerError;
    public string Message { get; init; } = "An unexpected error occurred.";
    public string? TraceId { get; init; }
    public object? Details { get; init; }
}

/// <summary>
/// PascalCase short tokens for <see cref="ApiErrorResponse.Code"/>. Kept as
/// constants so controllers, middleware, and tests reference the same strings.
/// </summary>
public static class ApiErrorCodes
{
    public const string BadRequest = "BadRequest";
    public const string Unauthorized = "Unauthorized";
    public const string Forbidden = "Forbidden";
    public const string NotFound = "NotFound";
    public const string Conflict = "Conflict";
    public const string ClientClosedRequest = "ClientClosedRequest";
    public const string InternalServerError = "InternalServerError";
}
