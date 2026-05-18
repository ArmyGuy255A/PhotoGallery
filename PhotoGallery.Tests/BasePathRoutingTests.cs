using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PhotoGallery.Data;

namespace PhotoGallery.Tests;

/// <summary>
/// xUnit integration coverage for Story S1 of the configurable-base-path epic
/// (#160). Validates three behaviors:
///
///   A. When ConfigurationSettings:BasePath=/photogallery is set, the app
///      serves under the prefix and returns 404 at the bare root path.
///   B. When BasePath is empty (raw local dev), the app serves at root.
///   C. When BasePath is set AND an upstream proxy sends X-Forwarded-Proto:
///      https, the ForwardedHeaders middleware rewrites
///      HttpContext.Request.Scheme to "https".
///
/// Uses Microsoft.AspNetCore.Mvc.Testing's WebApplicationFactory&lt;Program&gt;
/// to host the real Program.cs pipeline in-process. The fixture replaces
/// SqlServer with an EF Core InMemory provider so tests don't need a live
/// database. The HealthController exposes /api/healthz returning the current
/// request scheme; that endpoint is the probe surface for all three tests.
/// </summary>
public class BasePathRoutingTests
{
    private static WebApplicationFactory<Program> CreateFactory(string basePath)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Test");

                // Inject test config: BasePath under test, plus the minimum
                // settings required for Program.cs to build (JWT key, Google
                // client placeholders, DISABLE_AUTH).
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["BasePath"] = basePath,
                        ["DISABLE_AUTH"] = "true",
                        ["WorkersEnabled"] = "false",
                        ["Authentication:Jwt:Key"] = "test-key-test-key-test-key-test-key-1234567890",
                        ["Authentication:Jwt:Issuer"] = "PhotoGalleryTest",
                        ["Authentication:Jwt:Audience"] = "PhotoGalleryTestClient",
                        ["Google:ClientId"] = "test-client-id",
                        ["Google:ClientSecret"] = "test-client-secret",
                        ["Email:Provider"] = "mock",
                        ["ConnectionStrings:DefaultConnection"] = "Server=ignored;Database=ignored",
                    });
                });

                // Swap SqlServer DbContext for an EF Core InMemory provider —
                // the production registration's options lambda is never invoked
                // because we remove it before resolution.
                builder.ConfigureServices(services =>
                {
                    var dbContextDescriptors = services
                        .Where(d => d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>)
                                 || d.ServiceType == typeof(ApplicationDbContext))
                        .ToList();
                    foreach (var d in dbContextDescriptors)
                    {
                        services.Remove(d);
                    }

                    services.AddDbContext<ApplicationDbContext>(o =>
                        o.UseInMemoryDatabase($"basepath-tests-{Guid.NewGuid()}"));
                });
            });
    }

    [Fact]
    public async Task A_BasePathSet_HealthzServedUnderPrefix_RootReturns404()
    {
        await using var factory = CreateFactory("/photogallery");
        var client = factory.CreateClient();

        var underPrefix = await client.GetAsync("/photogallery/api/healthz");
        Assert.Equal(HttpStatusCode.OK, underPrefix.StatusCode);

        var atRoot = await client.GetAsync("/api/healthz");
        Assert.Equal(HttpStatusCode.NotFound, atRoot.StatusCode);
    }

    [Fact]
    public async Task B_BasePathEmpty_HealthzServedAtRoot()
    {
        await using var factory = CreateFactory("");
        var client = factory.CreateClient();

        var atRoot = await client.GetAsync("/api/healthz");
        Assert.Equal(HttpStatusCode.OK, atRoot.StatusCode);
    }

    [Fact]
    public async Task C_BasePathSet_XForwardedProtoHttps_SchemeRewrittenToHttps()
    {
        await using var factory = CreateFactory("/photogallery");
        var client = factory.CreateClient();

        var req = new HttpRequestMessage(HttpMethod.Get, "/photogallery/api/healthz");
        req.Headers.Add("X-Forwarded-Proto", "https");
        req.Headers.Add("X-Forwarded-Host", "appeid.app");

        var res = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var body = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var scheme = doc.RootElement.GetProperty("scheme").GetString();
        Assert.Equal("https", scheme);
    }
}
