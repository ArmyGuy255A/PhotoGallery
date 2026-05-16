using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PhotoGallery.Middleware;
using Xunit;

namespace PhotoGallery.Tests;

/// <summary>
/// Behaviour contract for <see cref="ExceptionHandlingMiddleware"/>: every
/// known exception type maps to a known (status, code) pair, every response
/// body matches the <see cref="ApiErrorResponse"/> envelope, and
/// <c>details</c> is populated only in Development / Trial.
/// </summary>
public class ExceptionHandlingMiddlewareTests
{
    private static IHostEnvironment Env(string name)
    {
        var env = new Mock<IHostEnvironment>();
        env.SetupGet(e => e.EnvironmentName).Returns(name);
        return env.Object;
    }

    private static async Task<(int status, ApiErrorResponse body)> InvokeAsync(
        Exception toThrow, string envName)
    {
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();
        ctx.TraceIdentifier = "trace-abc-123";

        RequestDelegate next = _ => throw toThrow;
        var mw = new ExceptionHandlingMiddleware(
            next,
            NullLogger<ExceptionHandlingMiddleware>.Instance,
            Env(envName));

        await mw.InvokeAsync(ctx);

        ctx.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await JsonSerializer.DeserializeAsync<ApiErrorResponse>(
            ctx.Response.Body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        return (ctx.Response.StatusCode, body!);
    }

    [Fact]
    public async Task ArgumentException_MapsTo_400_BadRequest()
    {
        var (status, body) = await InvokeAsync(new ArgumentException("missing field"), "Production");

        Assert.Equal(StatusCodes.Status400BadRequest, status);
        Assert.Equal(ApiErrorCodes.BadRequest, body.Code);
        Assert.Equal("missing field", body.Message);
        Assert.Equal("trace-abc-123", body.TraceId);
        Assert.Null(body.Details);
    }

    [Fact]
    public async Task KeyNotFoundException_MapsTo_404_NotFound()
    {
        var (status, body) = await InvokeAsync(new KeyNotFoundException("album x"), "Production");

        Assert.Equal(StatusCodes.Status404NotFound, status);
        Assert.Equal(ApiErrorCodes.NotFound, body.Code);
        Assert.Equal("trace-abc-123", body.TraceId);
    }

    [Fact]
    public async Task UnauthorizedAccessException_MapsTo_403_Forbidden()
    {
        var (status, body) = await InvokeAsync(new UnauthorizedAccessException("nope"), "Production");

        Assert.Equal(StatusCodes.Status403Forbidden, status);
        Assert.Equal(ApiErrorCodes.Forbidden, body.Code);
        // User-safe message, never the raw "nope".
        Assert.DoesNotContain("nope", body.Message);
    }

    [Fact]
    public async Task OperationCanceledException_MapsTo_499_ClientClosedRequest()
    {
        var (status, body) = await InvokeAsync(new OperationCanceledException(), "Production");

        Assert.Equal(ExceptionHandlingMiddleware.StatusClientClosedRequest, status);
        Assert.Equal(ApiErrorCodes.ClientClosedRequest, body.Code);
    }

    [Fact]
    public async Task UnknownException_MapsTo_500_InternalServerError()
    {
        var (status, body) = await InvokeAsync(new InvalidOperationException("boom"), "Production");

        Assert.Equal(StatusCodes.Status500InternalServerError, status);
        Assert.Equal(ApiErrorCodes.InternalServerError, body.Code);
        // Production: details MUST be null so internals do not leak.
        Assert.Null(body.Details);
        // Production: raw exception message MUST NOT appear in the user-safe summary.
        Assert.DoesNotContain("boom", body.Message);
    }

    [Fact]
    public async Task Trial_Environment_Populates_Details_With_StackTrace()
    {
        var (_, body) = await InvokeAsync(new InvalidOperationException("kaboom"), "Trial");

        Assert.NotNull(body.Details);
        var json = JsonSerializer.Serialize(body.Details);
        Assert.Contains("InvalidOperationException", json);
        Assert.Contains("kaboom", json);
    }

    [Fact]
    public async Task Development_Environment_Populates_Details()
    {
        var (_, body) = await InvokeAsync(new InvalidOperationException("dev"), "Development");

        Assert.NotNull(body.Details);
    }
}
