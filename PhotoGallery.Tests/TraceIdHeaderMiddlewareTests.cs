using Microsoft.AspNetCore.Http;
using PhotoGallery.Middleware;
using Xunit;

namespace PhotoGallery.Tests;

/// <summary>
/// Confirms <see cref="TraceIdHeaderMiddleware"/> writes the
/// <c>X-Trace-Id</c> header to every response so the SPA can echo the id back
/// in user-reported bug captures regardless of success / failure.
/// </summary>
public class TraceIdHeaderMiddlewareTests
{
    [Fact]
    public async Task SuccessfulRequest_WritesXTraceIdHeader()
    {
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();
        ctx.TraceIdentifier = "00-trace-success-01";

        RequestDelegate next = c =>
        {
            // Trigger OnStarting by writing a byte.
            c.Response.StatusCode = StatusCodes.Status200OK;
            return c.Response.WriteAsync("ok");
        };

        var mw = new TraceIdHeaderMiddleware(next);
        await mw.InvokeAsync(ctx);

        Assert.True(ctx.Response.Headers.ContainsKey(TraceIdHeaderMiddleware.HeaderName));
        Assert.Equal("00-trace-success-01",
            ctx.Response.Headers[TraceIdHeaderMiddleware.HeaderName].ToString());
    }

    [Fact]
    public async Task ErrorResponse_StillCarriesXTraceIdHeader()
    {
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();
        ctx.TraceIdentifier = "00-trace-error-02";

        RequestDelegate next = c =>
        {
            c.Response.StatusCode = StatusCodes.Status500InternalServerError;
            return c.Response.WriteAsync("error");
        };

        var mw = new TraceIdHeaderMiddleware(next);
        await mw.InvokeAsync(ctx);

        Assert.Equal("00-trace-error-02",
            ctx.Response.Headers[TraceIdHeaderMiddleware.HeaderName].ToString());
    }

    [Fact]
    public async Task DoesNotOverwriteExistingHeader()
    {
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();
        ctx.TraceIdentifier = "auto-generated";
        ctx.Response.Headers[TraceIdHeaderMiddleware.HeaderName] = "explicit-id";

        RequestDelegate next = c => c.Response.WriteAsync("ok");

        var mw = new TraceIdHeaderMiddleware(next);
        await mw.InvokeAsync(ctx);

        Assert.Equal("explicit-id",
            ctx.Response.Headers[TraceIdHeaderMiddleware.HeaderName].ToString());
    }
}
