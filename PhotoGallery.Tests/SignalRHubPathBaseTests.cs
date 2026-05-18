using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using Authentication.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using PhotoGallery.Data;

namespace PhotoGallery.Tests;

/// <summary>
/// xUnit integration coverage for Story S2 of the configurable-base-path epic
/// (#161). Validates SignalR's <c>PhotoProgressHub</c> mounts correctly under
/// the configured <c>BasePath</c>:
///
///   A. With BasePath=/photogallery, POST /photogallery/hubs/photo-progress/
///      negotiate?negotiateVersion=1 returns 200 with a JSON body containing
///      <c>connectionId</c>.
///   B. With BasePath=/photogallery, POST /hubs/photo-progress/negotiate
///      returns 404 (the 404-outside-BasePath middleware S1 introduced
///      enforces this).
///   C. With BasePath empty, POST /hubs/photo-progress/negotiate returns 200
///      (raw-dev shape preserved).
///   D. With BasePath=/photogallery, if the negotiate response contains a
///      <c>url</c> field (transport-dependent), it does NOT start with
///      <c>/hubs/</c>: SignalR's emitted URL must respect the path base.
///
/// Auth approach (chosen: approach **A** from issue #161):
///   The hub carries <c>[Authorize]</c>. The RED iteration confirmed that
///   <c>DISABLE_AUTH=true</c> (approach B) does NOT satisfy <c>[Authorize]</c>
///   in a WAF host: <c>UseAuthorization</c>'s PolicyEvaluator re-authenticates
///   via the explicit JwtBearer scheme and ignores the principal that
///   <c>DisableAuthMiddleware</c> attached to <c>HttpContext.User</c>. We
///   therefore mint a real HS256 JWT via the in-process
///   <see cref="JwtTokenService"/> using the same Issuer/Audience/Key the
///   factory configures, and attach it as a <c>Bearer</c> header. JwtBearer
///   validates against the symmetric key and the request is accepted by the
///   <c>[Authorize]</c> policy.
///
/// Uses Microsoft.AspNetCore.Mvc.Testing's WebApplicationFactory&lt;Program&gt;
/// to host the real Program.cs pipeline in-process with an EF Core InMemory
/// provider swapped in for SqlServer.
/// </summary>
[Collection(BasePathEnvVarsCollection.Name)]
public class SignalRHubPathBaseTests
{
    /// <summary>
    /// Mirrors <see cref="BasePathRoutingTests.CreateFactory"/>. See that
    /// class's XML doc for the rationale behind setting env vars BEFORE
    /// constructing the factory (Program.cs reads its configuration snapshot
    /// during synchronous Main, before WAF's ConfigureAppConfiguration
    /// callbacks fire).
    /// </summary>
    private static WebApplicationFactory<Program> CreateFactory(string basePath)
    {
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
                        o.UseInMemoryDatabase($"signalr-hub-tests-{Guid.NewGuid()}"));
                });
            });
    }

    private static string MintTestJwt(WebApplicationFactory<Program> factory)
    {
        // Resolve JwtTokenService from the test host's container — it is
        // wired with the same Issuer/Audience/Key the JwtBearer scheme
        // validates against (both read from ConfigurationSettings), so a
        // token issued here passes signature + issuer + audience checks.
        using var scope = factory.Services.CreateScope();
        var tokenSvc = scope.ServiceProvider.GetRequiredService<JwtTokenService>();
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new(ClaimTypes.Email, "s2-test@localhost"),
            new(ClaimTypes.Name, "s2-test@localhost"),
        };
        return tokenSvc.GenerateToken(claims);
    }

    private static HttpRequestMessage NegotiatePost(string path, string token) =>
        new(HttpMethod.Post, $"{path}?negotiateVersion=1")
        {
            Content = new StringContent(string.Empty),
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) },
        };

    [Fact]
    public async Task A_BasePathSet_NegotiateUnderPrefix_Returns200WithConnectionId()
    {
        await using var factory = CreateFactory("/photogallery");
        var token = MintTestJwt(factory);
        var client = factory.CreateClient();

        var res = await client.SendAsync(NegotiatePost("/photogallery/hubs/photo-progress/negotiate", token));
        var body = await res.Content.ReadAsStringAsync();

        Assert.True(res.StatusCode == HttpStatusCode.OK,
            $"Expected 200, got {(int)res.StatusCode}. Body: {body}");

        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.TryGetProperty("connectionId", out var connId),
            $"Response missing connectionId. Body: {body}");
        Assert.False(string.IsNullOrWhiteSpace(connId.GetString()));
    }

    [Fact]
    public async Task B_BasePathSet_NegotiateAtBareRoot_Returns404()
    {
        await using var factory = CreateFactory("/photogallery");
        var token = MintTestJwt(factory);
        var client = factory.CreateClient();

        var res = await client.SendAsync(NegotiatePost("/hubs/photo-progress/negotiate", token));
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task C_BasePathEmpty_NegotiateAtRoot_Returns200()
    {
        await using var factory = CreateFactory("");
        var token = MintTestJwt(factory);
        var client = factory.CreateClient();

        var res = await client.SendAsync(NegotiatePost("/hubs/photo-progress/negotiate", token));
        var body = await res.Content.ReadAsStringAsync();

        Assert.True(res.StatusCode == HttpStatusCode.OK,
            $"Expected 200, got {(int)res.StatusCode}. Body: {body}");

        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.TryGetProperty("connectionId", out var connId));
        Assert.False(string.IsNullOrWhiteSpace(connId.GetString()));
    }

    [Fact]
    public async Task D_BasePathSet_NegotiateResponse_UrlIfPresent_DoesNotBypassBasePath()
    {
        await using var factory = CreateFactory("/photogallery");
        var token = MintTestJwt(factory);
        var client = factory.CreateClient();

        var res = await client.SendAsync(NegotiatePost("/photogallery/hubs/photo-progress/negotiate", token));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var body = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);

        // SignalR's negotiate v1 response is typically:
        //   { connectionId, connectionToken, negotiateVersion, availableTransports }
        // It does NOT include a "url" field in the same-origin case; a "url"
        // field would only appear on a redirect-style negotiate response. If
        // present, it MUST respect the path base (i.e. not start with /hubs/).
        if (doc.RootElement.TryGetProperty("url", out var urlEl))
        {
            var url = urlEl.GetString();
            Assert.False(string.IsNullOrEmpty(url));
            Assert.False(url!.StartsWith("/hubs/", StringComparison.Ordinal),
                $"negotiate emitted absolute path '{url}' that bypasses BasePath '/photogallery'.");
        }
    }
}
