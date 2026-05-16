using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace PhotoGallery.Middleware;

/// <summary>
/// Helpers that produce the uniform <see cref="ApiErrorResponse"/> envelope
/// from the two ASP.NET hook points where the framework returns errors
/// without going through <see cref="ExceptionHandlingMiddleware"/>:
///
/// <list type="number">
///   <item>
///     <c>[ApiController]</c> model-binding 400s, plumbed via
///     <c>ApiBehaviorOptions.InvalidModelStateResponseFactory</c>.
///   </item>
///   <item>
///     Unmatched <c>/api/*</c> routes, plumbed via
///     <c>endpoints.MapFallback("/api/{*rest}", …)</c>.
///   </item>
/// </list>
/// </summary>
public static class ApiErrorResponseFactory
{
    /// <summary>
    /// Build a 400-shaped envelope from a model-state-invalid context. Picks
    /// the first model error per field, joins them with "; " so the SPA gets a
    /// single human-readable summary without needing to enumerate the errors
    /// dictionary. The full per-field errors map is kept under
    /// <c>details.errors</c> for the SPA's eventual inline-validation UI.
    /// </summary>
    public static IActionResult ValidationProblem(ActionContext context)
    {
        var traceId = context.HttpContext.TraceIdentifier;

        var errors = context.ModelState
            .Where(kvp => kvp.Value!.Errors.Count > 0)
            .ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value!.Errors.Select(e => e.ErrorMessage).ToArray());

        var summary = string.Join("; ",
            errors.SelectMany(kvp => kvp.Value).Where(m => !string.IsNullOrWhiteSpace(m)));
        if (string.IsNullOrWhiteSpace(summary))
        {
            summary = "The request was invalid.";
        }

        var body = new ApiErrorResponse
        {
            Code = ApiErrorCodes.BadRequest,
            Message = summary,
            TraceId = traceId,
            Details = new { errors }
        };

        return new ObjectResult(body)
        {
            StatusCode = StatusCodes.Status400BadRequest,
            ContentTypes = { "application/json" }
        };
    }

    /// <summary>
    /// Build a 404-shaped envelope for unmatched API routes.
    /// </summary>
    public static Task WriteNotFoundAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        context.Response.ContentType = "application/json; charset=utf-8";
        var body = new ApiErrorResponse
        {
            Code = ApiErrorCodes.NotFound,
            Message = $"No API route matches '{context.Request.Path}'.",
            TraceId = context.TraceIdentifier
        };
        return System.Text.Json.JsonSerializer.SerializeAsync(
            context.Response.Body,
            body,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
    }
}
