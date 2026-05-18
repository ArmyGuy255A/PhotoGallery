using Authentication;
using Authentication.Services;
using Azure.Identity;
using Configuration;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PhotoGallery;
using PhotoGallery.Data;
using PhotoGallery.Data.Repositories;
using PhotoGallery.Interfaces;
using PhotoGallery.Middleware;
using PhotoGallery.Models;
using PhotoGallery.Services;
using PhotoGallery.Services.Email;
using PhotoGallery.Services.Processing;
using PhotoGallery.Services.Storage;
using PhotoGallery.Hubs;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);

// -----------------------------------------------------------------------------
// Local-only, gitignored overlays. Loaded after appsettings.{Environment}.json
// so they override committed defaults, and before Key Vault so KV still wins
// for production secrets when configured.
//   1. appsettings.Local.json              — env-agnostic (e.g. Google OAuth)
//   2. appsettings.{Environment}.Local.json — env-specific (e.g. KV URI)
// -----------------------------------------------------------------------------
builder.Configuration.AddJsonFile(
    "appsettings.Local.json",
    optional: true,
    reloadOnChange: true);
builder.Configuration.AddJsonFile(
    $"appsettings.{builder.Environment.EnvironmentName}.Local.json",
    optional: true,
    reloadOnChange: true);

// -----------------------------------------------------------------------------
// Azure Key Vault as a configuration source (opt-in).
//
// Activated only when KeyVault:Uri is non-empty so the all-local stack, xUnit
// tests, and CI never reach Azure. DefaultAzureCredential transparently picks
// up `az login` credentials locally and Managed Identity when running in Azure.
//
// Configuration precedence (highest wins):
//   1. Environment variables          (e.g. ConnectionStrings__DefaultConnection)
//   2. appsettings.{Environment}.json (e.g. appsettings.Trial.json)
//   3. Azure Key Vault                (when KeyVault:Uri is set)
//   4. appsettings.json
//
// Key Vault secret naming: use double-dash to nest, e.g.
//   "ConnectionStrings--DefaultConnection" → ConnectionStrings:DefaultConnection.
// Coordinated with the platform engineer (see Documentation/Runbooks/local-azure-dev.md).
// -----------------------------------------------------------------------------
var keyVaultUri = builder.Configuration["KeyVault:Uri"];
if (!string.IsNullOrWhiteSpace(keyVaultUri))
{
    builder.Configuration.AddAzureKeyVault(
        new Uri(keyVaultUri),
        new DefaultAzureCredential());
}

// Bridge Key-Vault-canonical secret names to the strongly-typed binding paths
// the rest of the codebase already consumes (see ConfigurationCanonicalAliases).
// Single source of truth for the cross-branch naming contract with the
// platform engineer's Terraform module.
ConfigurationCanonicalAliases.BridgeKeyVaultCanonicalNames(
    builder.Configuration, builder.Configuration);

// Bind strongly-typed configuration first so downstream registrations can use it.
// Reference: clean-architecture-guide skill — "Cross-Cutting Concerns Live in Sub-Projects"
builder.Services.AddConfigurationServices(builder.Configuration, out var settings);

// Configure Serilog.
//
// Built programmatically (not from appsettings.json) so the sink set is
// guaranteed: a misconfigured JSON `Using`/`WriteTo` block silently disables
// all sinks, which is what blinded us when the ACA-deployed image stopped
// emitting logs. Console always wins (ACA's log collector tails stdout) and
// Application Insights is added when its connection string env var is set
// (Trial / production via ACA).
//
// Serilog.Sinks.File was intentionally dropped: the previous JSON config
// pointed at a relative `PhotoGallery/Logs/photogallery-.log` that doesn't
// exist in the container and isn't useful when running in Azure anyway.
builder.Host.UseSerilog((context, services, configuration) =>
{
    configuration
        .MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
        .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
        .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
        .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
        .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Migrations", LogEventLevel.Information)
        .MinimumLevel.Override("Azure.Identity", LogEventLevel.Warning)
        .Enrich.FromLogContext()
        .WriteTo.Console(outputTemplate:
            "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] ({SourceContext}) {Message:lj}{NewLine}{Exception}");

    var aiConnectionString = context.Configuration["ApplicationInsights:ConnectionString"]
        ?? Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING");
    if (!string.IsNullOrWhiteSpace(aiConnectionString))
    {
        var telemetryConfig = new TelemetryConfiguration { ConnectionString = aiConnectionString };
        configuration.WriteTo.ApplicationInsights(telemetryConfig, TelemetryConverter.Traces);
    }
});

// Application Insights for request/dependency/exception telemetry. Reads
// APPLICATIONINSIGHTS_CONNECTION_STRING automatically; no-ops if unset so
// local dev / xUnit are unaffected.
builder.Services.AddApplicationInsightsTelemetry();

// Configure listening URLs. Defaults to http://0.0.0.0:5105 (not localhost) so
// the local docker stack (S6 of epic #159) can reach the backend via
// host.docker.internal from inside the nginx-appeid container — binding to
// 127.0.0.1 would refuse those connections. ASPNETCORE_URLS still wins when
// set (e.g. in containers / Trial / Production). Kestrel rejects
// http://localhost:0 (dynamic port) so don't go there.
var urls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "http://0.0.0.0:5105";
builder.WebHost.UseUrls(urls);

// Configure ForwardedHeaders so X-Forwarded-Proto / Host / For / Prefix from
// the upstream reverse proxy (nginx-appeid in front of /photogallery/*) are
// honored by Kestrel. Without this:
//   - Request.Scheme stays "http" behind a TLS-terminating proxy, breaking
//     server-emitted absolute URLs (SignalR negotiate redirects, OAuth
//     callbacks, generated links) and forcing UseHttpsRedirection into a
//     redirect loop.
//   - Request.PathBase never reflects the proxy's mount-prefix even when
//     X-Forwarded-Prefix is supplied.
//
// CRITICAL: clear KnownProxies + KnownNetworks. The ForwardedHeaders middleware
// silently *no-ops* in containers (where the proxy isn't 127.0.0.0/8) unless
// you opt out of the default allowlist. Both ACA (Trial/Prod) and the local
// docker stack put the proxy on a non-loopback bridge address, so the default
// allowlist would drop every header.
//
// Reference: epic #159 / story #160; ASP.NET Core forwarded-headers docs.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedProto |
        ForwardedHeaders.XForwardedHost |
        ForwardedHeaders.XForwardedPrefix;
    options.KnownProxies.Clear();
    options.KnownNetworks.Clear();
});

// EF Core: single context, SQL Server only. Local dev uses Docker SQL Server
// (`docker compose up -d mssql`), trial/prod use Azure SQL. See
// PhotoGallery/Data/DatabaseProviderSelector.cs for connection wiring.
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    DatabaseProviderSelector.Apply(options, builder.Configuration));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

// Configure CORS — AllowFrontendDev unions ConfigurationSettings.Frontend.Url
// with ConfigurationSettings.Cors.AllowedOrigins (env-bound via
// Cors__AllowedOrigins__N), deduplicates, and strips empties. Allows
// credentials so future cookie-based flows Just Work. AllowAnyOrigin is
// avoided because it's incompatible with AllowCredentials and weakens
// prod parity. Mirrors VerdantIQ's named-policy pattern.
var allowedOrigins = new[] { settings.Frontend.Url }
    .Concat(settings.Cors.AllowedOrigins ?? Enumerable.Empty<string>())
    .Where(o => !string.IsNullOrWhiteSpace(o))
    .Select(o => o.TrimEnd('/'))
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray();
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontendDev", cors =>
    {
        cors.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

// Configure JWT Bearer authentication via the Authentication sub-project.
// JwtTokenService and the JwtBearer scheme are both registered here.
// Reference: photogallery-auth-skill — JWT issuance + validation
builder.Services.AddAuthenticationServices(settings);

// Cookie + Google OAuth schemes are still wired here because they're used by the
// legacy server-side OAuth callback flow (AuthController.Login + GoogleCallback).
// Once the frontend fully switches to the GIS popup → /api/auth/external-login flow,
// these can be removed.
builder.Services.AddAuthentication()
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddGoogle(google =>
    {
        google.ClientId = settings.Google.ClientId;
        google.ClientSecret = settings.Google.ClientSecret;
    });

builder.Services.AddScoped<IExternalAuthService, ExternalAuthService>();

// Register storage provider based on configuration
builder.Services.AddSingleton<IStorageProvider>(sp =>
{
    try
    {
        return StorageProviderFactory.Create(builder.Configuration, sp);
    }
    catch (Exception ex)
    {
        var logger = sp.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "Failed to create storage provider. Check your storage configuration.");
        throw;
    }
});

// Register repositories
builder.Services.AddScoped<ApplicationDbContextInitializer>();
builder.Services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
builder.Services.AddScoped<IAlbumRepository, AlbumRepository>();
builder.Services.AddScoped<IPhotoRepository, PhotoRepository>();
builder.Services.AddScoped<IPhotoVersionUrlRepository, PhotoVersionUrlRepository>();
builder.Services.AddScoped<IAccessCodeRepository, AccessCodeRepository>();
builder.Services.AddScoped<IUserCartRepository, UserCartRepository>();
builder.Services.AddScoped<IProcessingQueueRepository, ProcessingQueueRepository>();
builder.Services.AddScoped<IProcessingQueueItemRepository, ProcessingQueueItemRepository>();
builder.Services.AddScoped<IAuditLogRepository, AuditLogRepository>();

// Register services
builder.Services.AddScoped<PhotoVersionUrlService>();
builder.Services.AddScoped<ICartZipService, CartZipService>();
builder.Services.AddSingleton<WatermarkService>();
builder.Services.AddScoped<IWatermarkTextResolver, WatermarkTextResolver>();
builder.Services.AddScoped<IWatermarkBackfillService, WatermarkBackfillService>();
builder.Services.AddScoped<GdprService>();
builder.Services.AddScoped<IUserDisplayNameResolver, UserDisplayNameResolver>();

// Register image processing service as singleton (manages its own scopes for background worker)
builder.Services.AddSingleton<IImageProcessor, ImageProcessingService>();

// Register email service via configuration-driven factory (mock for dev/tests, Azure for prod).
var emailProvider = builder.Configuration["Email:Provider"] ?? "mock";
if (emailProvider.Equals("azure", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<IEmailService, AzureCommunicationEmailService>();
}
else
{
    builder.Services.AddSingleton<IEmailService, MockEmailService>();
}

// Register consistency checker for validating photo processing completion
builder.Services.AddScoped<PhotoConsistencyChecker>();

// Register storage/DB consistency reconciliation (D007).
// Scoped because the per-cycle StorageConsistencyWorker resolves it per tick
// and the admin endpoint resolves it per request. The internal SemaphoreSlim
// is per-instance which is sufficient for the single-process deployment model.
builder.Services.AddScoped<StorageConsistencyService>();

// Register the orphaned-blob reaper (Phase 5). Scoped to match the existing
// reconciliation services so the per-tick worker scope and per-request admin
// endpoint share identical lifetimes. The internal SemaphoreSlim serializes
// in-process invocations; multi-replica safety relies on DeleteIfExists
// idempotency at the storage layer.
builder.Services.AddScoped<OrphanedBlobReaperService>();
builder.Services.AddScoped<ChaosStorageService>();
builder.Services.AddScoped<FailedPhotoPurgeService>();

// Register background services for photo processing and URL refresh.
//
// Scale-out topology: the same image runs in two modes, gated by the
//   WorkersEnabled environment variable / config key.
//     true  (default) — process runs API + background workers (single-replica dev / trial).
//     false           — process runs API only; background workers do NOT register.
//                       Pair with a sibling "pg-worker" ACA app where WorkersEnabled=true
//                       and ingress is disabled, so:
//                         - API replicas stay responsive at 0.5 vCPU (no image processing
//                           starving the request thread pool)
//                         - worker replicas scale on queue depth via KEDA SQL custom metric.
//                       The ProcessingQueueItem lease (UPDLOCK + READPAST on SqlServer)
//                       makes N worker replicas safe — each replica claims its own batch.
var workersEnabled = builder.Configuration.GetValue("WorkersEnabled", true);
if (workersEnabled)
{
    builder.Services.AddHostedService<PhotoProcessingWorker>();
    builder.Services.AddHostedService<PhotoVersionUrlRefreshWorker>();
    builder.Services.AddHostedService<StorageConsistencyWorker>();
    builder.Services.AddHostedService<OrphanedBlobReaperWorker>();
}
else
{
    // API-only replicas run the scheduler that feeds the AdminJob queue.
    // We deliberately don't register this on worker replicas so the queue
    // isn't fed from multiple sources (would still be idempotent, but the
    // log trail stays clean). The scheduler enqueues routine reconcile
    // and reap jobs that workers pick up the same way as admin-clicked
    // ones — workers don't run their own timer cycles anymore.
    builder.Services.AddHostedService<AdminJobScheduler>();
}
builder.Services.AddSingleton<WorkerScheduleRegistry>();
builder.Services.AddSingleton<WorkerHeartbeatWriter>();
builder.Services.AddSingleton<AdminJobDispatcher>();
builder.Services.AddScoped<ISettingsResolver, SettingsResolver>();
builder.Services.AddMemoryCache();

builder.Services.AddDefaultIdentity<User>(options => options.SignIn.RequireConfirmedAccount = true)
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>();

// Restore JwtBearer as the default authentication scheme. AddDefaultIdentity
// (above) calls AddIdentityCookies() which silently overrides the
// DefaultAuthenticateScheme + DefaultChallengeScheme set by
// AddAuthenticationServices to its own cookie scheme. Without this, [Authorize]
// on API controllers would resolve against the (empty) Identity cookie
// principal — producing 403 on protected endpoints even when a valid Bearer
// JWT carrying the right roles is in the Authorization header.
//
// Symptom of the regression: GET /api/albums/{id}/access-codes returns 403
// despite OnTokenValidated logging roles=[Admin]. The JwtBearer scheme
// authenticated the token but [Authorize(Roles="Admin")] used the
// IdentityApplicationScheme (now empty) for its principal lookup.
builder.Services.Configure<AuthenticationOptions>(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
});

builder.Services.AddControllersWithViews()
    .AddJsonOptions(o =>
    {
        // Always emit DateTime as ISO 8601 UTC with Z suffix so the SPA's
        // Angular DatePipe renders them in the visitor's local timezone.
        // See Serialization/UtcDateTimeJsonConverter.cs for the rationale
        // (EF Core hands back Kind=Unspecified, which made the default
        // serializer omit the timezone offset and the browser interpret
        // the string as local time).
        o.JsonSerializerOptions.Converters.Add(new PhotoGallery.Serialization.UtcDateTimeJsonConverter());
        o.JsonSerializerOptions.Converters.Add(new PhotoGallery.Serialization.NullableUtcDateTimeJsonConverter());
    });

// Override ASP.NET's default ProblemDetails-shaped 400 response so model
// binding / validation failures come back in the same envelope the global
// ExceptionHandlingMiddleware emits. Without this, controllers decorated with
// [ApiController] auto-generate { title, status, type, errors } responses that
// don't match the SPA's error-toast contract.
builder.Services.Configure<Microsoft.AspNetCore.Mvc.ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = PhotoGallery.Middleware.ApiErrorResponseFactory.ValidationProblem;
});

// SignalR hub for real-time photo-processing progress (Phase 3).
// JWT auth on the WebSocket handshake is wired via the OnMessageReceived
// callback in Authentication/DependencyInjection.cs — browsers can't set
// custom headers on the WS upgrade so the SPA passes the JWT in the
// ?access_token=... query string for paths under /hubs/.
builder.Services.AddSignalR()
    .AddJsonProtocol(o =>
    {
        // Mirror the REST controllers — SignalR clients (the FE
        // PhotoProgressService) also need UTC-with-Z timestamps so
        // toLocaleString() / DatePipe render correctly.
        o.PayloadSerializerOptions.Converters.Add(new PhotoGallery.Serialization.UtcDateTimeJsonConverter());
        o.PayloadSerializerOptions.Converters.Add(new PhotoGallery.Serialization.NullableUtcDateTimeJsonConverter());
    });

var app = builder.Build();

// Initialize database — migrations + seed.
//
// HARD FAIL on error: a running container with an un-migrated database
// 500s every authenticated request because the Identity tables are missing,
// and that failure was previously invisible because the exception got
// swallowed here. Bubbling out terminates the process; ACA's startup probe
// then marks the revision unhealthy so the deployment is flagged rather
// than silently broken.
try
{
    await app.InitializeDatabaseAsync();
}
catch (Exception ex)
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogCritical(ex, "Database initialization failed. Aborting startup.");
    Log.CloseAndFlush();
    Environment.Exit(1);
}

// Configure the HTTP request pipeline.
//
// ForwardedHeaders MUST run before everything else so the rewritten Scheme,
// Host, RemoteIpAddress, and PathBase are visible to every downstream
// middleware (HTTPS redirection, routing, auth, exception handler). PathBase
// then strips the proxy mount-prefix off Request.Path so MVC routing matches
// against the bare /api/... template the controllers declare. Both must come
// before UseRouting (per the order documented on the issue).
app.UseForwardedHeaders();

if (!string.IsNullOrEmpty(settings.BasePath))
{
    app.UsePathBase(settings.BasePath);
}

if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

// Trace-id surfacing + global API exception handling.
//
// Both run only for /api/* requests so the legacy MVC / Razor surfaces
// continue to use app.UseExceptionHandler("/Home/Error") (see above). The
// trace-id middleware MUST be outside the exception middleware so the header
// is written via OnStarting even when the exception middleware rewrites the
// response (Response.Clear keeps OnStarting callbacks).
//
// Ordering note: both are wired BEFORE UseAuthentication so authentication
// failures (e.g. token validation throws) are caught by the same envelope and
// the trace id reaches the wire.
app.UseWhen(ctx => ctx.Request.Path.StartsWithSegments("/api"), apiBranch =>
{
    apiBranch.UseMiddleware<PhotoGallery.Middleware.TraceIdHeaderMiddleware>();
    apiBranch.UseMiddleware<PhotoGallery.Middleware.ExceptionHandlingMiddleware>();
});

// Enable CORS (before authentication)
app.UseCors("AllowFrontendDev");

// Required by Google Identity Services popup flow:
// the popup uses window.postMessage back to the opener, which browsers
// block when the opener's Cross-Origin-Opener-Policy is "same-origin".
// "same-origin-allow-popups" keeps cross-origin isolation for the rest
// of the page while permitting the GIS popup to communicate.
// See Documentation/Architecture/AUTHENTICATION.md → COOP for GIS popup.
app.Use(async (context, next) =>
{
    context.Response.Headers["Cross-Origin-Opener-Policy"] = "same-origin-allow-popups";
    await next();
});

// Add DISABLE_AUTH middleware (before UseAuthentication)
app.UseMiddleware<DisableAuthMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapRazorPages()
    .WithStaticAssets();

// Map the photo-progress SignalR hub. Authenticated via JwtBearer; the
// SPA passes the JWT as ?access_token=... on the WS upgrade. See
// Authentication/DependencyInjection.cs → OnMessageReceived.
app.MapHub<PhotoProgressHub>("/hubs/photo-progress");

// Fallback for unmatched /api/* routes so 404s come back in the uniform
// envelope rather than the framework's empty 404. Registered after all
// controller / hub mappings so it only catches genuinely unmapped paths.
app.MapFallback("/api/{*rest}", PhotoGallery.Middleware.ApiErrorResponseFactory.WriteNotFoundAsync);

// Image processing is driven by PhotoProcessingWorker (a BackgroundService
// registered via AddHostedService). The legacy in-service polling loop
// inside ImageProcessingService.StartProcessingWorkerAsync used to run in
// parallel here, which produced approximate-not-strict priority ordering:
// two workers concurrently leased batches, and when worker B fired while
// worker A held locks on the highest-priority Thumbnail rows, READPAST
// skipped those locked rows and worker B's lease started returning lower-
// priority Medium / High items. The user-visible symptom was all quality
// bars advancing in parallel rather than thumbnails finishing first.
// Removed the second start; PhotoProcessingWorker is the sole driver now.

app.Run();

// Expose the implicit Program type as partial so PhotoGallery.Tests can
// reference it with WebApplicationFactory<Program>. The xUnit project already
// has InternalsVisibleTo (see PhotoGallery.csproj) which lets it see the
// auto-generated internal class. This declaration just gives WAF a concrete
// type to bind generics against — no runtime behavior added.
public partial class Program { }
