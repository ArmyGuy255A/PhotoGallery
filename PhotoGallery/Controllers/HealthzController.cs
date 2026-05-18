using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace PhotoGallery.Controllers;

/// <summary>
/// Lightweight liveness probe at <c>/api/healthz</c>. Anonymous, allocation-
/// minimal, and intentionally cheap so reverse-proxy upstream health checks
/// (and the BasePath xUnit suite, see PhotoGallery.Tests/BasePathRoutingTests)
/// can hit it without exercising the database, storage provider, or auth
/// pipeline.
///
/// The response includes <c>scheme</c> so callers (and tests) can observe the
/// effective <see cref="HttpRequest.Scheme"/> after the ForwardedHeaders
/// middleware runs — useful for verifying that an upstream proxy's
/// <c>X-Forwarded-Proto: https</c> header has been honored end-to-end.
///
/// Reference: epic #159 / story #160.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[AllowAnonymous]
public class HealthzController : ControllerBase
{
    [HttpGet("")]
    public IActionResult Get()
    {
        return Ok(new
        {
            ok = true,
            scheme = Request.Scheme,
            pathBase = Request.PathBase.HasValue ? Request.PathBase.Value : string.Empty,
            host = Request.Host.Value,
        });
    }
}
