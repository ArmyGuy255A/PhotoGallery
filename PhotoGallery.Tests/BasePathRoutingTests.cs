using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
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
[Collection(BasePathEnvVarsCollection.Name)]
public class BasePathRoutingTests
{
    /// <summary>
    /// The entry-point in <c>Program.cs</c> calls
    /// <c>builder.Services.AddConfigurationServices(builder.Configuration, ...)</c>
    /// during the synchronous Main path, which runs BEFORE
    /// <see cref="IWebHostBuilder.ConfigureAppConfiguration"/> callbacks
    /// supplied via <see cref="WebApplicationFactory{T}.WithWebHostBuilder"/>
    /// are invoked. That means an in-memory config source registered via WAF
    /// never reaches the settings snapshot used to wire JWT, CORS, etc.
    ///
    /// Environment variables, however, are part of the default configuration
    /// chain that <c>WebApplication.CreateBuilder</c> registers up-front, so
    /// setting them BEFORE constructing the factory is the supported way to
    /// inject test config. xUnit runs tests inside a single class
    /// sequentially (parallelism is per-collection), so concurrent tests in
    /// this class can't trample each other's env vars.
    /// </summary>
    private static WebApplicationFactory<Program> CreateFactory(string basePath)
    {
        // Minimum config needed for Program.cs to build successfully under
        // ASPNETCORE_ENVIRONMENT=Test (no appsettings.Test.json connection
        // string, no Key Vault, no real OAuth).
        Environment.SetEnvironmentVariable("BasePath", basePath);
        Environment.SetEnvironmentVariable("DISABLE_AUTH", "true");
        Environment.SetEnvironmentVariable("WorkersEnabled", "false");
        Environment.SetEnvironmentVariable("Authentication__Jwt__Key",
            "test-key-test-key-test-key-test-key-1234567890");
        Environment.SetEnvironmentVariable("Authentication__Jwt__Issuer", "PhotoGalleryTest");
        Environment.SetEnvironmentVariable("Authentication__Jwt__Audience", "PhotoGalleryTestClient");
        Environment.SetEnvironmentVariable("Google__ClientId", "test-client-id");
        Environment.SetEnvironmentVariable("Google__ClientSecret", "test-client-secret");
        Environment.SetEnvironmentVariable("Email__Provider", "mock");
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection",
            "Server=ignored;Database=ignored");

        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Test");

                // Swap SqlServer DbContext for an EF Core InMemory provider.
                // We must remove every EF Core registration tied to the
                // SqlServer provider (DbContextOptions, the open-generic
                // IDbContextOptionsConfiguration, and the context itself);
                // otherwise EF detects two providers in the DI container and
                // throws InvalidOperationException at first context use.
                builder.ConfigureServices(services =>
                {
                    var efDescriptors = services
                        .Where(d =>
                            d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>)
                            || d.ServiceType == typeof(DbContextOptions)
                            || d.ServiceType == typeof(ApplicationDbContext)
                            || (d.ServiceType.IsGenericType
                                && d.ServiceType.GetGenericTypeDefinition()
                                    == typeof(IDbContextOptionsConfiguration<>)))
                        .ToList();
                    foreach (var d in efDescriptors)
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
